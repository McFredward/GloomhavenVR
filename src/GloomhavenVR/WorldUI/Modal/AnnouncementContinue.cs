using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// World-space input for the native level-up card announcement. Flat clicks reach
/// ClickTrackerExtended.Update through MouseClickLeft, not through uGUI. Without this bridge,
/// the revealed card waits forever and UILevelUpWindow deliberately keeps its inventory disabled.
/// Advancing the tracker preserves its sound, SkipNextClick, animation and native card-selection
/// continuation. It does not award a card, dismiss the level-up window or complete a level-up.
/// </summary>
internal static class AnnouncementContinue
{
    private static readonly AnnouncementContinueView View = new();
    private static UILevelUpWindow? _owner;
    private static bool _enabled;
    private static int _lastConfirmFrame = -1;

    internal static bool CanConfirm => _enabled && WorldUIConfig.ConversionActive
        && !FlatScreen.ManualScreenActive
        && !(Singleton<ESCMenu>.IsInitialized && Singleton<ESCMenu>.Instance.IsOpen)
        && _owner != null && _owner.isActiveAndEnabled
        && Singleton<UILevelUpWindow>.IsInitialized
        && ReferenceEquals(_owner, Singleton<UILevelUpWindow>.Instance)
        && IsLive(_owner.myWindow) && _owner.IsShowing
        && !_owner.isPlayingOpenAnimation && !_owner.isOpenConfirmationBox
        && _owner.enableTracker && _owner.nextCardTracker != null
        && _owner.nextCardTracker.gameObject.activeInHierarchy
        && _owner.character != null
        && (!FFSNetwork.IsOnline || _owner.character.IsUnderMyControl);

    private static bool IsLive(UIWindow? window) => window != null && window.isActiveAndEnabled
        && window.IsOpen;

    internal static void Tick(bool enabled)
    {
        _enabled = enabled;
        UILevelUpWindow? owner = enabled && Singleton<UILevelUpWindow>.IsInitialized
            ? Singleton<UILevelUpWindow>.Instance : null;
        if (owner == null || !owner.isActiveAndEnabled || !IsLive(owner.myWindow) || !owner.IsShowing)
        {
            _owner = null;
            View.Dispose();
            return;
        }
        _owner = owner;
        View.Tick(owner, CanConfirm, TryConfirm);
    }

    internal static bool TryConfirm()
    {
        if (!CanConfirm || _lastConfirmFrame == Time.frameCount) return false;
        _lastConfirmFrame = Time.frameCount;
        // enableTracker is the native reveal-completion latch. Tracker.enabled alone is not a
        // readiness test: OnControllerUnfocused clears it while preserving enableTracker. The
        // VR pointer may address this still-visible native announcement without controller focus.
        // ProcessClick consumes SkipNextClick and OnCardShown clears enableTracker synchronously,
        // before animating the card out. Repeated clicks therefore cannot skip a card or animation.
        _owner!.nextCardTracker.ProcessClick();
        return true;
    }
}
