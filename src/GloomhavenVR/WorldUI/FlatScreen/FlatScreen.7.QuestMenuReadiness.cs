using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    private static GLOOM.MainMenu.MainMenuUIManager? _questPresentedMenu;
    private GLOOM.MainMenu.MainMenuUIManager? _questProducedMenu;
    private Graphic? _questProducedMenuGraphic;
    private RenderTexture? _questProducedMenuTarget;
    private int _questMenuProducedFrame = -1;
    private bool _questMenuProbePending;
    private float _questNextMenuProbe;
    private int _questMenuProbeEpoch, _questMenuPositiveProbes, _questMenuProbeReports;
    private GLOOM.MainMenu.MainMenuUIManager? _questPixelMenu;
    private RenderTexture? _questPixelTarget;
    private Renderer? _questPixelConsumer;
    private int _questPixelFrame = -1;
    private Camera? _questProducedMenuCamera;
    private MenuImageProof? _questGraphicPixels;
    private float _questGraphicPixelsUntil;
    private readonly Dictionary<int, float> _questBlankGraphics = new();
    private bool _questMenuSourceProofLogged, _questMenuStaleProofLogged;

    private sealed class MenuImageProof
    {
        private readonly Graphic _graphic;
        private readonly Material _material;
        private readonly Shader _shader;
        private readonly Texture _texture;
        private readonly Sprite? _sprite;
        private readonly Color _color, _tint;
        internal MenuImageProof(Graphic graphic)
        {
            _graphic = graphic; _material = graphic.materialForRendering;
            _shader = _material.shader; _texture = graphic.mainTexture;
            _sprite = ((Image)graphic).overrideSprite;
            _color = graphic.color;
            _tint = _material.HasProperty("_Color") ? _material.GetColor("_Color") : Color.white;
        }
        internal bool Matches(Graphic? graphic) => graphic != null && graphic == _graphic
            && graphic.materialForRendering == _material && _material != null && _material.shader == _shader
            && graphic.mainTexture == _texture && ((Image)graphic).overrideSprite == _sprite
            && graphic.color == _color
            && (!_material.HasProperty("_Color") || _material.GetColor("_Color") == _tint);
    }

    // OnWillRenderObject is an admission callback, not a rendered picture.
    // B712 acknowledges transparent/off-screen/overwritten captures and black
    // consumers. During initial handoff only, render the ACTUAL consumer into a
    // tiny private target outside the head callback and asynchronously inspect it.
    // Never wait, read the full eye, drive loading, or block native interaction.
    private void ObserveQuestMenuFrame()
    {
        if (!QuestStandalonePlatform.Enabled || QuestMenuPresented || !_visible || _questMenuProbePending
            || Time.unscaledTime < _questNextMenuProbe || _questProducedMenu == null
            || _questProducedMenu != GLOOM.MainMenu.MainMenuUIManager.Instance
            || _questProducedMenuGraphic == null || _questProducedMenuTarget == null
            || Time.frameCount - _questMenuProducedFrame > 2) return;
        Camera? head = VRRigDriver.HeadCamera;
        Renderer? consumer = _quadRenderer;
        if (head == null || consumer == null || !consumer.enabled || !consumer.gameObject.activeInHierarchy
            || consumer.sharedMaterial == null || consumer.sharedMaterial.mainTexture != _questProducedMenuTarget) return;
        _questNextMenuProbe = Time.unscaledTime + .125f;
        if (!SystemInfo.supportsAsyncGPUReadback) return;
        RenderTexture? probe = null;
        try
        {
            Matrix4x4 view = head.stereoEnabled ? head.GetStereoViewMatrix(Camera.StereoscopicEye.Left) : head.worldToCameraMatrix;
            Matrix4x4 projection = head.stereoEnabled ? head.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left) : head.projectionMatrix;
            var sourceBinding = new MenuImageProof(_questProducedMenuGraphic);
            bool sourceStep = _questGraphicPixels == null || !_questGraphicPixels.Matches(_questProducedMenuGraphic)
                || Time.unscaledTime > _questGraphicPixelsUntil;
            probe = sourceStep ? RenderQuestGraphicProbe(_questProducedMenuGraphic, _questProducedMenuCamera)
                : RenderQuestConsumerProbe(consumer, consumer.sharedMaterial, view, projection);
            if (probe == null) return;
            var menu = _questProducedMenu;
            var target = _questProducedMenuTarget;
            var graphic = _questProducedMenuGraphic;
            var material = consumer.sharedMaterial;
            float requestedAt = Time.unscaledTime;
            int epoch = _questMenuProbeEpoch;
            RenderTexture retained = probe;
            _questMenuProbePending = true;
            AsyncGPUReadback.Request(retained, 0, TextureFormat.RGBA32, request =>
            {
                try
                {
                    if (epoch != _questMenuProbeEpoch) return;
                    int visible = 0;
                    if (!request.hasError)
                    {
                        var pixels = request.GetData<Color32>();
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            Color32 pixel = pixels[i];
                            if (pixel.a > 6 && (pixel.r > 6 || pixel.g > 6 || pixel.b > 6)) visible++;
                        }
                    }
                    bool owned = _visible && _questProducedMenu == menu && _questProducedMenuTarget == target
                        && _questProducedMenuGraphic == graphic && sourceBinding.Matches(graphic) && graphic != null && graphic.isActiveAndEnabled
                        && !graphic.canvasRenderer.cull && graphic.canvasRenderer.GetInheritedAlpha() > .01f
                        && VRRigDriver.HeadCamera == head && head != null && Time.unscaledTime - requestedAt <= .5f
                        && Time.frameCount - _questMenuProducedFrame <= 2
                        && _quadRenderer == consumer && consumer != null && consumer.enabled && consumer.gameObject.activeInHierarchy
                        && consumer.sharedMaterial == material && material != null && material.mainTexture == target;
                    if (!owned || visible < 3)
                    {
                        // Distinguish a blank decorative Image from an otherwise
                        // valid GPU result whose owner/target/frame expired.
                        // B716 retained neither a positive source proof nor the
                        // reason its receipt never completed. Share the existing
                        // four-record budget; no per-frame failure stream.
                        if (!owned && !_questMenuStaleProofLogged && Core.VRLog.Wants(Core.VRLogLevel.Debug))
                        {
                            _questMenuStaleProofLogged = true;
                            if (_questMenuProbeReports++ < 4)
                                UnityEngine.Debug.Log("[Quest startup] presentation spinner-final stage=menu-proof result=stale"
                                    + " source=" + sourceStep + " failed=" + request.hasError + " visiblePixels=" + visible
                                    + " ageSeconds=" + (Time.unscaledTime - requestedAt)
                                    + " producerAgeFrames=" + (Time.frameCount - _questMenuProducedFrame)
                                    + " sameHead=" + (VRRigDriver.HeadCamera == head)
                                    + " sameMenu=" + (_questProducedMenu == menu && menu == GLOOM.MainMenu.MainMenuUIManager.Instance)
                                    + " sameGraphic=" + (_questProducedMenuGraphic == graphic && sourceBinding.Matches(graphic))
                                    + " graphicVisible=" + (graphic != null && graphic.isActiveAndEnabled
                                        && !graphic.canvasRenderer.cull && graphic.canvasRenderer.GetInheritedAlpha() > .01f)
                                    + " sameConsumer=" + (_quadRenderer == consumer && consumer != null
                                        && consumer.sharedMaterial == material && material != null && material.mainTexture == target)
                                    + " finalXrPixelsObserved=false");
                        }
                        ClearQuestMenuPixelReceipt();
                        if (owned && sourceStep)
                        {
                            if (_questBlankGraphics.Count >= 64) _questBlankGraphics.Clear();
                            _questBlankGraphics[graphic!.GetInstanceID()] = Time.unscaledTime + 1f;
                        }
                        return;
                    }
                    if (sourceStep)
                    {
                        _questGraphicPixels = sourceBinding;
                        _questGraphicPixelsUntil = Time.unscaledTime + .5f;
                        if (!_questMenuSourceProofLogged && Core.VRLog.Wants(Core.VRLogLevel.Debug))
                        {
                            _questMenuSourceProofLogged = true;
                            if (_questMenuProbeReports++ < 4)
                                UnityEngine.Debug.Log("[Quest startup] presentation spinner-final stage=menu-source result=eligible"
                                    + " frame=" + Time.frameCount + " visiblePixels=" + visible
                                    + " privateEligibility=true finalXrPixelsObserved=false");
                        }
                        return;
                    }
                    if (_questGraphicPixels == null || !_questGraphicPixels.Matches(graphic)
                        || Time.unscaledTime > _questGraphicPixelsUntil) return;
                    _questMenuPositiveProbes++;
                    if (_questMenuPositiveProbes >= 2)
                    {
                        _questPixelMenu = menu; _questPixelTarget = target; _questPixelConsumer = consumer;
                        _questPixelFrame = Time.frameCount;
                    }
                    if (Core.VRLog.Wants(Core.VRLogLevel.Debug) && _questMenuProbeReports++ < 4)
                        UnityEngine.Debug.Log("[Quest startup] presentation spinner-final stage=menu-consumer"
                            + " frame=" + Time.frameCount + " visiblePixels=" + visible
                            + " consecutive=" + _questMenuPositiveProbes + " width=48 height=32 finalXrPixelsObserved=false");
                }
                finally
                {
                    retained.Release(); Object.Destroy(retained);
                    if (epoch == _questMenuProbeEpoch) _questMenuProbePending = false;
                }
            });
            probe = null;
        }
        catch (System.Exception error)
        {
            _questMenuProbePending = false; ClearQuestMenuPixelReceipt();
            if (Core.VRLog.Wants(Core.VRLogLevel.Debug) && _questMenuProbeReports++ < 4)
                UnityEngine.Debug.LogWarning("[Quest startup] presentation spinner-final stage=menu-consumer unavailable=" + error.GetType().Name);
        }
        finally { if (probe != null) { probe.Release(); Object.Destroy(probe); } }
    }

    private void ClearQuestMenuPixelReceipt()
    {
        _questMenuPositiveProbes = 0; _questPixelMenu = null; _questPixelTarget = null;
        _questPixelConsumer = null; _questPixelFrame = -1;
        _questGraphicPixels = null; _questGraphicPixelsUntil = 0f;
    }

    private void ReleaseQuestMenuReceipt()
    {
        if (_questPresentedMenu == _questProducedMenu) _questPresentedMenu = null;
        _questMenuProbeEpoch++; _questMenuProbePending = false;
        _questNextMenuProbe = 0f; _questProducedMenuCamera = null; _questBlankGraphics.Clear();
        _questProducedMenu = null; _questProducedMenuGraphic = null; _questProducedMenuTarget = null;
        ClearQuestMenuPixelReceipt();
    }

    // A menu instance/state is not a visible picture. Keep this bound to both
    // original Show/alpha and the actual capture -> head consumer submission.
    // Tiny asynchronous consumer readback precedes admission; no delayed native
    // continuation, synthetic Show or native state mutation.
    internal static bool QuestMenuPresented => QuestStandalonePlatform.Enabled
        && ScreenVisible && _questPresentedMenu != null
        && _questPresentedMenu == GLOOM.MainMenu.MainMenuUIManager.Instance;

    private void ObserveQuestMenuProducer(Camera camera)
    {
        if (!QuestStandalonePlatform.Enabled || QuestMenuPresented || !_visible || camera == null
            || !CapturedSet.TryGetValue(camera, out CapturedCamera captured)
            || !captured.IsUi || TargetFor(captured) == null
            || camera.targetTexture != TargetFor(captured)) return;
        GLOOM.MainMenu.MainMenuUIManager? menu = GLOOM.MainMenu.MainMenuUIManager.Instance;
        if (menu == null || !menu.Initialised) return;
        Graphic? graphic = VisibleNativeMenuWindow(menu, camera);
        if (graphic == null) return;
        if (_questProducedMenu != menu || _questProducedMenuGraphic != graphic
            || _questProducedMenuTarget != camera.targetTexture)
        {
            _questMenuProbeEpoch++; _questMenuProbePending = false;
            ClearQuestMenuPixelReceipt();
        }
        _questProducedMenuCamera = camera;
        _questProducedMenu = menu;
        _questProducedMenuGraphic = graphic;
        _questProducedMenuTarget = camera.targetTexture;
        _questMenuProducedFrame = Time.frameCount;
    }

    private Graphic? VisibleNativeMenuWindow(GLOOM.MainMenu.MainMenuUIManager menu, Camera camera) =>
        VisibleNativeMenuWindow(menu.mainMenu, camera)
        ?? VisibleNativeMenuWindow(menu.eulaScreen, camera)
        ?? VisibleNativeMenuWindow(menu.initialInteractionScreen, camera);

    private Graphic? VisibleNativeMenuWindow(Component? component, Camera camera)
    {
        if (component == null || !component.gameObject.activeInHierarchy) return null;
        UIWindow? window = component.GetComponent<UIWindow>();
        CanvasGroup? group = component.GetComponent<CanvasGroup>();
        if (window == null || !window.IsOpen || group == null || group.alpha <= .01f) return null;
        // B711's receipt accepted a merely-open window even when its capture
        // contained no menu geometry at all. IsOpen and a local CanvasGroup alpha
        // precede the native canvas build; an ancestor can also keep it invisible.
        // Admit an original submitted Graphic, not a menu state or a timer. This
        // bounded scan runs only during the one initial menu handover, never in
        // steady-state rendering. The consumer readback below also rejects a
        // blank capture or black final material; this state alone is insufficient.
        foreach (Graphic graphic in component.GetComponentsInChildren<Graphic>(false))
        {
            Canvas? canvas = graphic.canvas;
            if (_questBlankGraphics.TryGetValue(graphic.GetInstanceID(), out float until) && Time.unscaledTime < until) continue;
            if (graphic.GetType() != typeof(Image) || !graphic.isActiveAndEnabled || graphic.color.a <= .01f || canvas == null || !canvas.isActiveAndEnabled
                || graphic.canvasRenderer.cull || graphic.canvasRenderer.materialCount == 0
                || graphic.canvasRenderer.GetInheritedAlpha() <= .01f
                || (camera.cullingMask & (1 << graphic.gameObject.layer)) == 0
                || canvas.renderMode == RenderMode.ScreenSpaceOverlay
                || (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera != camera)
                || graphic.rectTransform.rect.width <= 0f || graphic.rectTransform.rect.height <= 0f) continue;
            return graphic;
        }
        return null;
    }

    private sealed class QuestMenuDrawWitness : MonoBehaviour
    {
        internal FlatScreen? Owner;

        private void OnWillRenderObject()
        {
            FlatScreen? owner = Owner;
            if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || owner == null
                || !owner._visible || Camera.current != VRRigDriver.HeadCamera
                || owner._questProducedMenu == null
                || owner._questProducedMenuGraphic == null || owner._questProducedMenuGraphic.canvasRenderer.cull
                || !owner._questProducedMenuGraphic.isActiveAndEnabled
                || owner._questProducedMenuGraphic.canvasRenderer.GetInheritedAlpha() <= .01f
                || owner._questPixelMenu != owner._questProducedMenu || owner._questPixelTarget != owner._questProducedMenuTarget
                || owner._questPixelConsumer != GetComponent<Renderer>() || Time.frameCount - owner._questPixelFrame > 30
                || owner._questProducedMenu != GLOOM.MainMenu.MainMenuUIManager.Instance
                || Time.frameCount - owner._questMenuProducedFrame > 1) return;
            Renderer? renderer = GetComponent<Renderer>();
            if (renderer == null || renderer.sharedMaterial == null
                || renderer.sharedMaterial.mainTexture != owner._questProducedMenuTarget) return;
            _questPresentedMenu = owner._questProducedMenu;
        }
    }
}
