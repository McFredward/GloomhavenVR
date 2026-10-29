using GLOOM.MainMenu;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Core.Events;

/// <summary>
/// The main menu becomes a place only after its original menu has actually appeared and
/// the first tracked pose has settled. A menu rig also exists throughout the studio movie,
/// loading and sign-in, so neither that rig nor MainMenuUIManager.Initialised proves arrival.
/// This latch describes presentation; it never creates a scenario/table or grants game input.
/// </summary>
internal static class MenuWorldContext
{
    private static MainMenuUIManager? _menu;
    private static Transform? _rig;
    private static Camera? _head;
    private static bool _ready;

    internal static uint Opening { get; private set; }

    internal static bool IsReady => _ready && VRSession.IsRunning
        && _rig != null && _rig == VRRigDriver.RigRoot
        && _head != null && _head == VRRigDriver.HeadCamera
        && _menu != null && _menu == MainMenuUIManager.Instance && _menu.isActiveAndEnabled
        && _menu.MainMenuCamera != null && _menu.MainMenuCamera.activeInHierarchy
        && !WorldUI.LoadingIndicator.FlatScreenSuppressed;

    // The ordinary 2D-map option also retains its environment without pretending it is
    // a 3D table. The native singleton is read only; no discovery/creation accessor runs.
    internal static bool AllowsSurroundings => VRSession.IsRunning
        && VRRigDriver.TrackingSettled
        && (VRModeStateMachine.TableInFrontOfPlayer || IsReady
            || (Singleton<global::MapChoreographer>.IsInitialized
                && Singleton<global::MapChoreographer>.Instance.isActiveAndEnabled));

    internal static void Tick(bool menuRig, bool trackingSettled)
    {
        MainMenuUIManager? menu = MainMenuUIManager.Instance;
        Transform? rig = VRRigDriver.RigRoot;
        Camera? head = VRRigDriver.HeadCamera;
        if (!menuRig || !trackingSettled || menu == null || !menu.isActiveAndEnabled
            || rig == null || head == null)
        {
            Reset();
            return;
        }
        if (menu != _menu || rig != _rig || head != _head)
        {
            Reset();
            _menu = menu; _rig = rig; _head = head;
        }
        if (_ready || menu.MainMenuCamera == null || !menu.MainMenuCamera.activeInHierarchy
            || menu.mainMenu == null || !menu.mainMenu.gameObject.activeInHierarchy
            || WorldUI.LoadingIndicator.FlatScreenSuppressed)
            return;
        UIWindow? window = menu.mainMenu.GetComponent<UIWindow>();
        if (window == null || !window.IsVisible)
            return;
        _ready = true;
        Opening++;
        VRLog.Info("Rig", "MENU WORLD READY: the original main menu is visible after tracked arrival; "
            + "surroundings, free movement and the independent movable window are admitted.");
    }

    internal static void Reset()
    {
        _ready = false;
        _menu = null; _rig = null; _head = null;
    }
}
