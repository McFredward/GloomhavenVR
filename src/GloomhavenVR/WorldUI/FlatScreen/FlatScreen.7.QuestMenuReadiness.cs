using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    private static GLOOM.MainMenu.MainMenuUIManager? _questPresentedMenu;
    private GLOOM.MainMenu.MainMenuUIManager? _questProducedMenu;
    private Graphic? _questProducedMenuGraphic;
    private RenderTexture? _questProducedMenuTarget;
    private int _questMenuProducedFrame = -1;

    // A menu instance/state is not a visible picture. Keep this bound to both
    // original Show/alpha and the actual capture -> head consumer submission.
    // No GPU readback, delayed timer, synthetic Show or native state mutation.
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
        _questProducedMenu = menu;
        _questProducedMenuGraphic = graphic;
        _questProducedMenuTarget = camera.targetTexture;
        _questMenuProducedFrame = Time.frameCount;
    }

    private static Graphic? VisibleNativeMenuWindow(GLOOM.MainMenu.MainMenuUIManager menu, Camera camera) =>
        VisibleNativeMenuWindow(menu.mainMenu, camera)
        ?? VisibleNativeMenuWindow(menu.eulaScreen, camera)
        ?? VisibleNativeMenuWindow(menu.initialInteractionScreen, camera);

    private static Graphic? VisibleNativeMenuWindow(Component? component, Camera camera)
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
        // steady-state rendering. No readback or synthetic native Show is needed.
        foreach (Graphic graphic in component.GetComponentsInChildren<Graphic>(false))
        {
            Canvas? canvas = graphic.canvas;
            if (!graphic.isActiveAndEnabled || graphic.color.a <= .01f || canvas == null || !canvas.isActiveAndEnabled
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
                || owner._questProducedMenu != GLOOM.MainMenu.MainMenuUIManager.Instance
                || Time.frameCount - owner._questMenuProducedFrame > 1) return;
            Renderer? renderer = GetComponent<Renderer>();
            if (renderer == null || renderer.sharedMaterial == null
                || renderer.sharedMaterial.mainTexture != owner._questProducedMenuTarget) return;
            _questPresentedMenu = owner._questProducedMenu;
        }
    }
}
