using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.Surfaces;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

internal static class NativeDecisionPromptSampler
{
    private static NativeDecisionPromptSnapshot? _last;
    private static readonly byte[] Scratch = new byte[NativeDecisionPromptCodec.MaxSize], Previous = new byte[NativeDecisionPromptCodec.MaxSize];
    private static int _previousSize;
    private static bool _refused;
    internal static readonly List<NativeDecisionPromptSnapshot> History = new(32);
    internal static void Reset() { _last = null; _previousSize = 0; History.Clear(); _refused = false; }

    internal static NativeDecisionPromptSnapshot? Sample()
    {
        UseBarsSurface.SampleDecisionAttribution(out int actor, out bool pending, out _);
        HelpBox? box = pending && WorldUI.WorldUIConfig.ConversionActive ? DamageTooltipSurface.NativePromptSource : null;
        NativeDecisionPromptState? state = null;
        try
        {
            WorldUI.ConvertedPanel? panel = DamageTooltipSurface.NativePromptPanel;
            if (box != null && panel != null && box.transform is RectTransform root
                && panel.HostRect.rect.width >= 1f && panel.HostRect.rect.height >= 1f)
            {
                var lines = new List<NativeDecisionPromptLine>(1);
                foreach (HelpBoxLine line in box.linesPool)
                {
                    if (line == null || !line.gameObject.activeSelf) continue;
                    lines.Add(new NativeDecisionPromptLine { Rect = Rect(line.transform as RectTransform),
                        Alpha = Alpha(line.transform), Flags = Flags(line.transform), Tip = Text(line.tipText), Warning = Text(line.warningTextAnimation) });
                }
                if (lines.Count != 0)
                    state = new NativeDecisionPromptState { Rect = Rect(root), Alpha = Alpha(root), Flags = Flags(root),
                        ReferencePixelsPerUnit = root.GetComponentInParent<Canvas>()?.referencePixelsPerUnit ?? 100f,
                        Frame = new[] { panel.HostRect.rect.width, panel.HostRect.rect.height,
                            (root.parent as RectTransform)?.rect.width ?? 0f, (root.parent as RectTransform)?.rect.height ?? 0f,
                            panel.FitContentPadding.x, panel.FitContentPadding.y }, Lines = lines.ToArray() };
            }
            // Compare encoded zero-time pictures so all actual text, geometry and renderer output
            // participate in the edge. A closed source sends one explicit clear, then stays quiet.
            var comparison = new NativeDecisionPromptSnapshot(0f, state != null ? actor : 0, state);
            int size = NativeDecisionPromptCodec.Write(comparison, Scratch);
            if (size == 0) throw new InvalidOperationException("native prompt exceeds the complete 4096-byte output budget");
            bool same = size == _previousSize;
            for (int i = 0; same && i < size; i++) same = Scratch[i] == Previous[i];
            if (same) return _last;
            Buffer.BlockCopy(Scratch, 0, Previous, 0, size); _previousSize = size;
            _last = new NativeDecisionPromptSnapshot(Time.unscaledTime, state != null ? actor : 0, state);
            if (History.Count == 32) History.RemoveAt(0); History.Add(_last);
            _refused = false; return _last;
        }
        catch (Exception e)
        {
            if (!_refused) { _refused = true; VRLog.Warn("Net", "NATIVE DECISION PROMPT: original output refused: " + e.Message); }
            // Never retain an older actor's sentence after a source failure.
            if (_last?.State != null) { _last = new NativeDecisionPromptSnapshot(Time.unscaledTime, 0, null); _previousSize = 0; }
            return _last;
        }
    }

    internal static float[] Rect(RectTransform? rt)
    {
        if (rt == null) throw new InvalidOperationException("original prompt rect missing");
        return new[] { rt.anchorMin.x, rt.anchorMin.y, rt.anchorMax.x, rt.anchorMax.y, rt.pivot.x, rt.pivot.y,
            rt.sizeDelta.x, rt.sizeDelta.y, rt.anchoredPosition3D.x, rt.anchoredPosition3D.y, rt.anchoredPosition3D.z,
            rt.localScale.x, rt.localScale.y, rt.localScale.z, rt.localRotation.x, rt.localRotation.y, rt.localRotation.z, rt.localRotation.w };
    }
    private static float Alpha(Transform node) => node.GetComponent<CanvasGroup>()?.alpha ?? 1f;
    private static byte Flags(Transform node)
    {
        CanvasGroup? group = node.GetComponent<CanvasGroup>();
        return (byte)((node.gameObject.activeSelf ? 1 : 0) | (group != null && group.enabled ? 2 : 0)
            | (group != null && group.ignoreParentGroups ? 4 : 0));
    }
    private static NativeDecisionPromptText Text(TMP_Text text)
    {
        if (text == null) throw new InvalidOperationException("original prompt text channel missing");
        Color color = text.color, renderer = text.GetComponent<CanvasRenderer>().GetColor();
        return new NativeDecisionPromptText {
            Rect = Rect(text.rectTransform), Alpha = Alpha(text.transform), GroupFlags = (byte)(Flags(text.transform) >> 1), Colors = new[] { color.r, color.g, color.b, color.a, renderer.r, renderer.g, renderer.b, renderer.a },
            Font = new[] { text.fontSize, text.fontSizeMin, text.fontSizeMax, text.characterSpacing, text.wordSpacing,
                text.lineSpacing, text.paragraphSpacing, (float)text.fontWeight },
            Margin = new[] { text.margin.x, text.margin.y, text.margin.z, text.margin.w },
            Flags = (ushort)((text.gameObject.activeSelf ? 1 : 0) | (text.enabled ? 2 : 0) | (text.enableAutoSizing ? 4 : 0)
                | (text.enableWordWrapping ? 8 : 0) | (text.richText ? 16 : 0)), Style = (ushort)text.fontStyle,
            Alignment = (int)text.alignment, Overflow = (byte)text.overflowMode,
            Text = DecisionLabelMask.Apply(text.text, out _) };
    }
}

internal static class NativeDecisionPromptRegistry
{
    private static readonly Dictionary<int, List<NativeDecisionPromptSnapshot>> Players = new();
    internal static void Set(int player, NativeDecisionPromptSnapshot snapshot)
    {
        if (!Players.TryGetValue(player, out var history)) Players[player] = history = new(32);
        if (history.Count != 0 && snapshot.SampleTime <= history[history.Count - 1].SampleTime) return;
        if (history.Count == 32) history.RemoveAt(0); history.Add(snapshot);
    }
    internal static List<NativeDecisionPromptSnapshot>? History(int player) => Players.TryGetValue(player, out var h) ? h : null;
    internal static NativeDecisionPromptSnapshot? Latest(int player)
    { var h = History(player); return h != null && h.Count != 0 ? h[h.Count - 1] : null; }
    internal static bool TryGet(int actorId, out NativeDecisionPromptSnapshot? snapshot)
    {
        snapshot = null;
        foreach (var pair in Players)
        {
            var h = pair.Value;
            if (h.Count != 0 && h[h.Count - 1].ActorId == actorId) { snapshot = h[h.Count - 1]; return true; }
        }
        return false;
    }
    internal static void Remove(int player) => Players.Remove(player);
    internal static void Reset() => Players.Clear();
}
