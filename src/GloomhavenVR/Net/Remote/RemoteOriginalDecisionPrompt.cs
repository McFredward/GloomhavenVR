using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.WorldUI.Surfaces;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Net;

/// <summary>Actual original HelpBox/HelpBoxLine output with source-time warning animation.</summary>
internal sealed class RemoteOriginalDecisionPrompt
{
    private readonly RemoteWidgetMirror _mirror;
    private readonly Transform _placement;
    private readonly List<Line> _targets = new(4);
    private int _mappedStamp = -1;
    private float _contentHeight;
    private GameObject? _stageHost;
    private RectTransform? _stage;
    private readonly List<Line> _lines = new(4);
    private readonly UseBarAnimationPlaybackClock _clock = new();
    private NativeDecisionPromptSnapshot? _identity;
    private bool _refused;
    private sealed class Line
    {
        internal RectTransform Root = null!;
        internal TMP_Text Tip = null!, Warning = null!;
    }
    internal bool Showing { get; private set; }
    internal float Height => Showing ? _contentHeight : 0f;

    internal RemoteOriginalDecisionPrompt(Transform mount)
    {
        _placement = new GameObject("NativePromptPaddingSeat").transform;
        _placement.SetParent(mount, false);
        _mirror = new RemoteWidgetMirror("OriginalDecisionPrompt", _placement, PlayTray.DecisionMountWidth,
            PlayTray.DecisionMountMaxHeight, new Vector2(0f, -1f), densityScale: DecisionDockSurface.DensityScale,
            driveFromSource: true, contentOutsideFrame: true);
    }

    internal void Show(CharacterDecisionPresentation? picture)
    {
        Showing = false;
        NativeDecisionPromptSnapshot? latest = picture?.NativePrompt;
        if (latest?.State == null) { _mirror.SetShown(false); _identity = null; return; }
        try
        {
            NativeDecisionPromptSnapshot from = latest, to = latest;
            List<NativeDecisionPromptSnapshot>? history = picture!.NativePromptHistory;
            int first = history?.Count ?? 0;
            if (history != null)
                for (int i = history.Count - 1; i >= 0 && NativeDecisionPromptSnapshot.SameIdentity(latest, history[i]); i--) first = i;
            if (_identity == null || !NativeDecisionPromptSnapshot.SameIdentity(_identity, latest))
                _clock.Reset(history != null && first < history.Count ? history[first].SampleTime : latest.SampleTime, Time.unscaledTime);
            _identity = latest;
            float cursor = _clock.Advance(Time.unscaledTime, latest.SampleTime);
            if (history != null)
                for (int i = first; i < history.Count; i++)
                {
                    if (history[i].SampleTime <= cursor) from = history[i];
                    if (history[i].SampleTime >= cursor) { to = history[i]; break; }
                }
            float progress = _clock.Progress(from.SampleTime, to.SampleTime);
            if (_stage == null || _lines.Count != latest.State.Lines.Length) Build(latest.State.Lines.Length);
            if (_stage == null) { _mirror.SetShown(false); return; }
            Paint(_stage, _lines, from.State!, to.State!, progress);
            float[] frame = to.State!.Frame;
            _mirror.SetOwnerFrame(new Vector2(frame[0], frame[1]), new Vector2(frame[2], frame[3]));
            _mirror.SetShown(true);
            if (!_mirror.Refresh(_stage)) return;
            _mirror.TickLive();
            // Mirror copy/layout cannot erase intermediate renderer colors, font spacing, or
            // warning transforms. Apply every captured channel on the inert original clone last.
            RectTransform? clone = _mirror.CloneOf(_stage) as RectTransform;
            if (clone == null) return;
            if (_mappedStamp != _mirror.RebuildStamp)
            {
                _targets.Clear();
                foreach (Line line in _lines)
                    _targets.Add(new Line { Root = (_mirror.CloneOf(line.Root) as RectTransform)!,
                        Tip = _mirror.CloneOf(line.Tip.transform)!.GetComponent<TMP_Text>(),
                        Warning = _mirror.CloneOf(line.Warning.transform)!.GetComponent<TMP_Text>() });
                _mappedStamp = _mirror.RebuildStamp;
            }
            Paint(clone, _targets, from.State!, to.State!, progress);
            float metersPerPixel = frame[1] > 0f ? _mirror.FittedSize.y / frame[1] : 0f;
            float padding = frame[5] * metersPerPixel;
            _placement.localPosition = Vector3.up * padding;
            _contentHeight = Mathf.Max(0f, _mirror.FittedSize.y - 2f * padding);
            if (_mirror.HostCanvas != null) _mirror.HostCanvas.referencePixelsPerUnit = to.State!.ReferencePixelsPerUnit;
            Showing = true; _refused = false;
        }
        catch (Exception e)
        {
            _mirror.SetShown(false);
            if (!_refused) { _refused = true; Core.VRLog.Warn("Net", "NATIVE DECISION PROMPT: original mirror withheld: " + e.Message); }
        }
    }

    private void Build(int count)
    {
        ReleaseStage();
        HelpBox? original = Singleton<HelpBox>.IsInitialized ? Singleton<HelpBox>.Instance : InitiativeTrack.Instance?.helpBox;
        if (original == null || original._boxLinePrefab == null) return;
        try
        {
            _stageHost = new GameObject("OriginalHelpBoxStage"); _stageHost.SetActive(false);
            GameObject stage = Object.Instantiate(original.gameObject, _stageHost.transform, false);
            _stage = stage.transform as RectTransform;
            foreach (HelpBoxLine pooled in stage.GetComponentsInChildren<HelpBoxLine>(true)) Object.DestroyImmediate(pooled.gameObject);
            for (int i = 0; i < count; i++)
            {
                GameObject go = Object.Instantiate(original._boxLinePrefab.gameObject, stage.transform, false);
                HelpBoxLine line = go.GetComponent<HelpBoxLine>();
                _lines.Add(new Line { Root = (RectTransform)go.transform, Tip = line.tipText, Warning = line.warningTextAnimation });
            }
            RemoteWidgetMirror.Neutralize(stage, RemoteWidgetMirror.LayoutOwner.Source, null);
            stage.SetActive(true);
        }
        catch { ReleaseStage(); throw; }
    }

    private static void Paint(RectTransform root, List<Line> targets, NativeDecisionPromptState a, NativeDecisionPromptState b, float t)
    {
        Rect(root, a.Rect, b.Rect, t); Group(root, Mathf.Lerp(a.Alpha, b.Alpha, t), (byte)(b.Flags >> 1));
        root.gameObject.SetActive((b.Flags & 1) != 0);
        for (int i = 0; i < targets.Count; i++)
        {
            Line target = targets[i]; NativeDecisionPromptLine x = a.Lines[i], y = b.Lines[i];
            Rect(target.Root, x.Rect, y.Rect, t); Group(target.Root, Mathf.Lerp(x.Alpha, y.Alpha, t), (byte)(y.Flags >> 1));
            target.Root.gameObject.SetActive((y.Flags & 1) != 0);
            Text(target.Tip, x.Tip, y.Tip, t); Text(target.Warning, x.Warning, y.Warning, t);
        }
    }
    private static void Text(TMP_Text target, NativeDecisionPromptText a, NativeDecisionPromptText b, float t)
    {
        Rect(target.rectTransform, a.Rect, b.Rect, t);
        target.gameObject.SetActive((b.Flags & 1) != 0); target.enabled = (b.Flags & 2) != 0;
        target.enableAutoSizing = (b.Flags & 4) != 0; target.enableWordWrapping = (b.Flags & 8) != 0;
        target.richText = (b.Flags & 16) != 0; target.fontStyle = (FontStyles)b.Style;
        target.alignment = (TextAlignmentOptions)b.Alignment; target.overflowMode = (TextOverflowModes)b.Overflow;
        target.fontSize = Mathf.Lerp(a.Font[0], b.Font[0], t); target.fontSizeMin = b.Font[1]; target.fontSizeMax = b.Font[2];
        target.characterSpacing = Mathf.Lerp(a.Font[3], b.Font[3], t); target.wordSpacing = Mathf.Lerp(a.Font[4], b.Font[4], t);
        target.lineSpacing = Mathf.Lerp(a.Font[5], b.Font[5], t); target.paragraphSpacing = Mathf.Lerp(a.Font[6], b.Font[6], t);
        target.fontWeight = (FontWeight)(int)b.Font[7];
        target.margin = new Vector4(b.Margin[0], b.Margin[1], b.Margin[2], b.Margin[3]);
        if (target.text != b.Text) target.text = b.Text;
        target.color = Color(a.Colors, b.Colors, 0, t);
        Group(target.transform, Mathf.Lerp(a.Alpha, b.Alpha, t), b.GroupFlags);
        target.ForceMeshUpdate(ignoreActiveState: true);
        target.GetComponent<CanvasRenderer>().SetColor(Color(a.Colors, b.Colors, 4, t));
    }
    private static Color Color(float[] a, float[] b, int at, float t) => new(Mathf.Lerp(a[at], b[at], t),
        Mathf.Lerp(a[at + 1], b[at + 1], t), Mathf.Lerp(a[at + 2], b[at + 2], t), Mathf.Lerp(a[at + 3], b[at + 3], t));
    private static void Group(Transform root, float alpha, byte flags)
    {
        CanvasGroup? group = root.GetComponent<CanvasGroup>();
        if (group == null && flags == 0 && alpha == 1f) return;
        if (group == null) group = root.gameObject.AddComponent<CanvasGroup>();
        group.enabled = (flags & 1) != 0; group.alpha = alpha; group.ignoreParentGroups = (flags & 2) != 0;
        group.interactable = false; group.blocksRaycasts = false;
    }
    private static void Rect(RectTransform target, float[] a, float[] b, float t)
    {
        float V(int i) => Mathf.Lerp(a[i], b[i], t);
        target.anchorMin = new Vector2(V(0), V(1)); target.anchorMax = new Vector2(V(2), V(3));
        target.pivot = new Vector2(V(4), V(5)); target.sizeDelta = new Vector2(V(6), V(7));
        target.anchoredPosition3D = new Vector3(V(8), V(9), V(10)); target.localScale = new Vector3(V(11), V(12), V(13));
        target.localRotation = Quaternion.Slerp(new Quaternion(a[14], a[15], a[16], a[17]), new Quaternion(b[14], b[15], b[16], b[17]), t);
    }
    private void ReleaseStage()
    {
        if (_stageHost != null) Object.Destroy(_stageHost);
        _stageHost = null; _stage = null; _lines.Clear(); _targets.Clear(); _mappedStamp = -1;
    }
    internal void Destroy() { _mirror.Destroy(); ReleaseStage(); Object.Destroy(_placement.gameObject); }
}
