using System;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>Native city pulse when gamepad presentation disables its original flat button.
/// The original animator recipe runs on a private, non-rendering visual stage. Game controllers,
/// input handlers and every serialized event are removed before the stage can activate.</summary>
internal sealed class MapCityEventPulse
{
    private GameObject? _stage;
    private GUIAnimator? _animator;
    private Image? _image;
    private bool _playing;
    private bool _refused;
    internal Image? Sample(UICityEncounterButton source, bool live)
    {
        if (source.gameObject.activeInHierarchy || !live)
        { if (_playing) _animator?.Stop(); _playing = false; return null; }
        if (_refused) return null;
        if (_stage == null)
        {
            try { Build(source); }
            catch (Exception e) { _refused = true; VRLog.Warn("MapRoom", "MAP CITY PULSE: native visual recipe unavailable: " + e.Message); return null; }
        }
        if (_animator == null || _image == null) return null;
        if (!_playing) { _animator.Play(); _playing = true; }
        return _image;
    }
    private void Build(UICityEncounterButton source)
    {
        GameObject stage = new("NativeCityPulseStage"); stage.SetActive(false);
        try
        {
            GameObject clone = Object.Instantiate(source.gameObject, stage.transform, false);
            var city = clone.GetComponent<UICityEncounterButton>();
            GUIAnimator? animator = MapCityEventSource.Field<GUIAnimator>(city, "highlightAnimator");
            if (animator is not LeanTweenGUIAnimator) throw new InvalidOperationException("city highlight is not the verified native tween recipe");
            foreach (NativeUseBarAnimationBinding binding in NativeUseBarAnimationBinding.Capture(animator, clone.transform))
                if (binding.Target != clone.transform && !binding.Target.IsChildOf(clone.transform))
                    throw new InvalidOperationException("city highlight targets outside its private clone");
            foreach (GUIAnimator animation in clone.GetComponentsInChildren<GUIAnimator>(true))
            { animation.OnAnimationStarted = new UnityEvent(); animation.OnAnimationStopped = new UnityEvent(); animation.OnAnimationFinished = new UnityEvent(); }
            for (int pass = 0; pass < 3; pass++)
            {
                Component[] components = clone.GetComponentsInChildren<Component>(true);
                for (int i = components.Length - 1; i >= 0; i--)
                {
                    Component c = components[i];
                    if (c == null || c is Transform || c is Graphic || c is CanvasRenderer || c is CanvasGroup || c is GUIAnimator) continue;
                    if (c is Canvas && pass == 0) continue;
                    Object.DestroyImmediate(c);
                }
            }
            _animator = animator; _image = animator.GetComponentInChildren<Image>(true);
            clone.SetActive(true); animator.gameObject.SetActive(true);
            stage.transform.position = new Vector3(0f, -100000f, 0f);
            _stage = stage; stage.SetActive(true);
        }
        catch { Object.Destroy(stage); throw; }
    }
    internal void Destroy() { _animator?.Stop(); if (_stage != null) Object.Destroy(_stage); _stage = null; _animator = null; _playing = false; }
}
