using GloomhavenVR.Cards;
using GloomhavenVR.WorldUI.Surfaces;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>Original HelpBoxLine presentation, stripped of behaviours before activation.</summary>
internal sealed class RemoteOriginalDecisionPrompt
{
    private readonly Transform _mount;
    private readonly RemoteWidgetMirror _mirror;
    private GameObject? _stageHost;
    private RectTransform? _stage;
    private TMP_Text? _text;
    private string? _last;
    internal bool Showing { get; private set; }
    internal float Height => Showing ? _mirror.FittedSize.y : 0f;

    internal RemoteOriginalDecisionPrompt(Transform mount)
    {
        _mount = mount;
        _mirror = new RemoteWidgetMirror("OriginalDecisionPrompt", mount, PlayTray.DecisionMountWidth,
            float.MaxValue, new Vector2(0f, -1f), densityScale: DecisionDockSurface.DensityScale,
            driveFromSource: true, contentOutsideFrame: true);
    }

    internal void Show(string? text)
    {
        Showing = false;
        if (string.IsNullOrEmpty(text)) { _mirror.SetShown(false); return; }
        if (_stage == null)
        {
            HelpBox? box = Singleton<HelpBox>.IsInitialized ? Singleton<HelpBox>.Instance : InitiativeTrack.Instance?.helpBox;
            HelpBoxLine? original = box?._boxLinePrefab;
            if (original == null) { _mirror.SetShown(false); return; }
            _stageHost = new GameObject("OriginalHelpLineStage"); _stageHost.SetActive(false);
            GameObject stage = Object.Instantiate(original.gameObject, _stageHost.transform, false);
            HelpBoxLine line = stage.GetComponent<HelpBoxLine>();
            _text = line.tipText;
            _stage = stage.transform as RectTransform;
            if (line.warningTextAnimation != null) line.warningTextAnimation.gameObject.SetActive(false);
            RemoteWidgetMirror.Neutralize(stage, RemoteWidgetMirror.LayoutOwner.CloneAtBoardOwnersWidth, null);
            stage.SetActive(true);
        }
        if (_last != text && _text != null) { _text.text = text; _last = text; }
        _mirror.SetShown(true);
        if (!_mirror.Refresh(_stage)) return;
        _mirror.TickLive(); Showing = true;
    }

    internal void Destroy()
    {
        _mirror.Destroy();
        if (_stageHost != null) Object.Destroy(_stageHost);
        _stageHost = null; _stage = null; _text = null;
    }
}
