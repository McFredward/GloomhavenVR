using GLOO.Introduction;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    internal static bool IsIntroductionWindow(UIWindow? window)
    {
        if (window == null || !Singleton<UIIntroductionManager>.IsInitialized)
            return false;
        UIIntroductionManager manager = Singleton<UIIntroductionManager>.Instance;
        return manager != null && manager.LayoutGroup != null
            && ReferenceEquals(manager.LayoutGroup.window, window);
    }

    /// <summary>
    /// Retire a standalone float before a composite records and moves its native content.
    /// Build 504 savegame logs show the introduction recording its home as its old modal host.
    /// Moving first left that host's grab bar, capture camera and fit writer alive: the empty
    /// frame remained visible while two layouts wrote the same hint. Release synchronously,
    /// before the composite measures anything, so it records the restored native home instead.
    /// This is a presentation handover, never a native Hide or a dismiss callback.
    /// </summary>
    internal static bool ReleaseForComposite(UIWindow window)
    {
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            if (!ReferenceEquals(wp.Window, window))
                continue;
            // A real user close must finish through the normal close path before adoption.
            if (wp.UserClosing)
                return false;

            Converted.RemoveAt(i);
            WindowMaterialise.DropPreRoll(wp.Panel, "the native window transfers into a composite");
            WindowMaterialise.Cancel(wp.Panel, "the native window transfers into a composite");
            wp.Grab?.Destroy();
            CanvasConversion.Release(wp.Panel);
        }

        // A native message can reopen while the previous standalone float dissolves. Tick removes
        // that float from Converted before PlayOut, but its pending callback still owns Target.
        // Build 506 recorded the dying mod host as the hint's home, then the callback took the
        // parked hint back. Retire that exact target's remaining conversion before adoption.
        for (int i = CanvasConversion.ActivePanels.Count - 1; i >= 0; i--)
        {
            if (i >= CanvasConversion.ActivePanels.Count)
                continue;
            ConvertedPanel panel = CanvasConversion.ActivePanels[i];
            if (!ReferenceEquals(panel.Target, window.transform))
                continue;
            WindowMaterialise.DropPreRoll(panel, "the native window transfers into a composite");
            WindowMaterialise.Cancel(panel, "the native window transfers into a composite");
            // Cancel invokes a pending vanish callback synchronously; it may already have released
            // this panel. Do not restore the same native target a second time.
            bool stillActive = false;
            for (int j = 0; j < CanvasConversion.ActivePanels.Count; j++)
                if (ReferenceEquals(CanvasConversion.ActivePanels[j], panel))
                    stillActive = true;
            if (stillActive)
                CanvasConversion.Release(panel);
        }

        ReleasePreConvertHide(window, "the composite takes over the native window");
        ReleaseScreenBind(window, "the composite takes over the native window");
        return window != null && window.transform != null;
    }
}
