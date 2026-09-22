using System;
using System.Collections.Generic;
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
    internal Transform? VisualRoot { get; private set; }
    private readonly List<Material> _materials = new();
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
            // A native fade setting may animate a material rather than Graphic.color. Isolate
            // every copied graphic material before capturing or running the original recipe.
            foreach (Graphic graphic in clone.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.material == null) continue;
                Material material = new(graphic.material); _materials.Add(material); graphic.material = material;
            }
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
            VisualRoot = clone.transform;
            clone.SetActive(true); animator.gameObject.SetActive(true);
            stage.transform.position = new Vector3(0f, -100000f, 0f);
            _stage = stage; stage.SetActive(true);
        }
        catch { Object.Destroy(stage); ReleaseMaterials(); throw; }
    }
    internal void Destroy()
    {
        _animator?.Stop();
        if (_stage != null) { _stage.SetActive(false); Object.Destroy(_stage); }
        ReleaseMaterials(); _stage = null; _animator = null; VisualRoot = null; _playing = false;
    }
    private void ReleaseMaterials() { foreach (Material material in _materials) Object.Destroy(material); _materials.Clear(); }

    internal static Vector3 Scale(Image image, Transform root)
    {
        // Include the animator holder and intermediate parents, not just Image.localScale.
        Vector3 scale = Vector3.one;
        for (Transform? t = image.transform; t != null; t = t.parent)
        { scale = Vector3.Scale(scale, t.localScale); if (ReferenceEquals(t, root)) break; }
        return scale;
    }
    internal static Color Color(Image image, Transform root)
    {
        Color color = image.color * image.canvasRenderer.GetColor();
        for (Transform? t = image.transform; t != null; t = t.parent)
        {
            CanvasGroup? group = t.GetComponent<CanvasGroup>();
            if (group != null && group.enabled) { color.a *= group.alpha; if (group.ignoreParentGroups) break; }
            if (ReferenceEquals(t, root)) break;
        }
        return color;
    }
}
