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

        ReleasePreConvertHide(window, "the composite takes over the native window");
        ReleaseScreenBind(window, "the composite takes over the native window");
        return window != null && window.transform != null;
    }
}
