using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    private static GLOOM.MainMenu.MainMenuUIManager? _questPresentedMenu;
    private GLOOM.MainMenu.MainMenuUIManager? _questProducedMenu;
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
        if (menu == null || !menu.Initialised || !VisibleNativeMenuWindow(menu)) return;
        _questProducedMenu = menu;
        _questMenuProducedFrame = Time.frameCount;
    }

    private static bool VisibleNativeMenuWindow(GLOOM.MainMenu.MainMenuUIManager menu) =>
        VisibleNativeMenuWindow(menu.mainMenu)
        || VisibleNativeMenuWindow(menu.eulaScreen)
        || VisibleNativeMenuWindow(menu.initialInteractionScreen);

    private static bool VisibleNativeMenuWindow(Component? component)
    {
        if (component == null || !component.gameObject.activeInHierarchy) return false;
        UIWindow? window = component.GetComponent<UIWindow>();
        CanvasGroup? group = component.GetComponent<CanvasGroup>();
        return window != null && window.IsOpen && group != null && group.alpha > .01f;
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
                || owner._questProducedMenu != GLOOM.MainMenu.MainMenuUIManager.Instance
                || Time.frameCount - owner._questMenuProducedFrame > 1) return;
            _questPresentedMenu = owner._questProducedMenu;
        }
    }
}
