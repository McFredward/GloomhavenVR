using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Net.Desync;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// Presents the original optional observer-retirement prompt in the 3D map room.
/// UIMapMultiplayerController.ConfirmRetirement creates a pending promise for another
/// player's character; only the guildmaster prompt's native click resolves it. The HUD
/// is deliberately absent here, so without this surface QueuedRetirements and the native
/// retirement ready barrier can wait forever. Keep the original widget, controllers,
/// animation and click callbacks. Showing it never confirms or commits a retirement.
/// </summary>
internal static class MapRetirementPrompt
{
    private static readonly FieldInfo? ButtonField = AccessTools.Field(typeof(UIGuildmasterConfirmActionButtonPresenter), "_button");
    private static readonly FieldInfo? PopupField = AccessTools.Field(typeof(UIGuildmasterConfirmActionPopupPresenter), "_popup");
    private static readonly FieldInfo? ButtonCallback = AccessTools.Field(typeof(UIGuildmasterConfirmActionButton), "_onConfirmCallback");
    private static readonly FieldInfo? PopupCallback = AccessTools.Field(typeof(UIGuildmasterConfirmActionPopup), "_onConfirmCallback");
    private static readonly FieldInfo? PopupBlocker = AccessTools.Field(typeof(UIGuildmasterConfirmActionPopup), "_partyPanelBlocker");
    private static readonly MethodInfo? PopupConfirm = AccessTools.Method(typeof(UIGuildmasterConfirmActionPopup), "Confirm");
    private static MonoBehaviour? _presenter, _source;
    private static Action? _callback;
    private static ConvertedPanel? _panel;
    private static GrabbableModal? _grab;
    private static GameObject? _click;
    private static bool _installed, _failed, _presentationEnabled;
    private static int _lastClickFrame = -1;

    internal static bool Visible => _panel?.IsAlive == true && _source != null
        && _source.gameObject.activeSelf;
    internal static bool OwnsGrab(GrabbableModal holder) => Visible && ReferenceEquals(_grab, holder);
    internal static bool ScreenFallbackWanted => _presentationEnabled && _failed
        && MapRoomDriver.Active && WorldUIConfig.ConversionActive && NativePromptStanding;

    internal static void Install()
    {
        if (_installed || VRSession.Harmony == null) return;
        VRSession.Harmony.PatchAll(typeof(RetirementPromptShownPatch));
        VRSession.Harmony.PatchAll(typeof(RetirementPromptReplacedPatch));
        _installed = true;
    }

    internal static void Capture(MonoBehaviour presenter, Action onConfirmCallback)
    {
        _presenter = presenter;
        _source = presenter is UIGuildmasterConfirmActionButtonPresenter
            ? ButtonField?.GetValue(presenter) as UIGuildmasterConfirmActionButton
            : presenter is UIGuildmasterConfirmActionPopupPresenter
                ? PopupField?.GetValue(presenter) as UIGuildmasterConfirmActionPopup : null;
        _callback = onConfirmCallback;
        _failed = false;
        _lastClickFrame = -1;
        if (_source == null)
            VRLog.Warn("MapRoom", "MAP RETIREMENT PROMPT: native presenter has no original widget; optional callback remains pending.");
    }

    private static bool NativePromptStanding => _presenter != null && _presenter.isActiveAndEnabled
        && _source != null && _source.isActiveAndEnabled && _callback != null
        && ReferenceEquals(_callback, (_source is UIGuildmasterConfirmActionButton
            ? ButtonCallback : PopupCallback)?.GetValue(_source));

    internal static void Tick(bool enabled)
    {
        _presentationEnabled = enabled;
        if (!enabled || !MapRoomDriver.Active || !WorldUIConfig.ConversionActive
            || !NativePromptStanding)
        {
            Reset();
            return;
        }
        if (_failed || FlatScreen.ManualScreenActive)
        {
            RestorePresentation(keepHit: true);
            if (_source?.transform is RectTransform original) AddConsoleHit(original);
            return;
        }
        if (_panel == null)
        {
            try { Build(); }
            catch (Exception error)
            {
                RestorePresentation(keepHit: true);
                _failed = true;
                VRLog.Warn("MapRoom", "MAP RETIREMENT PROMPT: original presentation conversion failed: "
                    + error.GetType().Name + "; native optional callback remains pending.");
                if (_source?.transform is RectTransform original) AddConsoleHit(original);
            }
        }
        _grab?.Tick();
    }

    private static void Build()
    {
        if (_source?.transform is not RectTransform target)
            throw new InvalidOperationException("Original prompt has no native rect");
        if (CanvasConversion.WorldCamera == null)
            throw new InvalidOperationException("Active map room has no WorldUI camera");
        // Convert the live original rather than instantiate a second native controller. The
        // ordinary converter records its native hierarchy and restores it on room standdown.
        _panel = CanvasConversion.Convert(target, "MapRetirementPrompt", fitContent: true,
            useModLayer: true) ?? throw new InvalidOperationException("Original prompt could not be converted");
        AddConsoleHit(target);
        Camera head = CanvasConversion.WorldCamera;
        PanelPlacement.Spawn(head, PanelLayout.WorldScale, out Vector3 position, out Quaternion rotation);
        _panel.HostGo.transform.SetPositionAndRotation(position, rotation);
        _grab = new GrabbableModal();
        _grab.Build(_panel, SharedWindowSizeLaw.ExtraScale(_panel.HostRect.sizeDelta,
            WorldUIConfig.CanvasScaleMm.Value), "Native retirement prompt");
        VRLog.Note("MapRoom", "MAP RETIREMENT PROMPT: original optional observer prompt is reachable; "
            + "a real player press still owns the native continuation.");
    }

    private static void AddConsoleHit(RectTransform target)
    {
        if (_source is not UIGuildmasterConfirmActionPopup) return;
        if (_click != null) { _click.layer = target.gameObject.layer; return; }
        // Console presentation has only a keyboard long-press adapter. Add an invisible
        // hit surface inside the original rect; original text/icon/hotkey artwork stays
        // unchanged. One deliberate VR click calls its original Confirm, after reading
        // the same native navigation blocker that guards its keyboard handler.
        _click = new GameObject("MapRetirementClick", typeof(RectTransform));
        _click.layer = target.gameObject.layer;
        RectTransform hit = (RectTransform)_click.transform;
        hit.SetParent(target, false);
        hit.anchorMin = Vector2.zero; hit.anchorMax = Vector2.one;
        hit.offsetMin = hit.offsetMax = Vector2.zero;
        Image image = _click.AddComponent<Image>();
        image.color = Color.clear; image.raycastTarget = true;
        _click.AddComponent<RetirementPromptClick>();
    }

    internal static bool TryPressConsole(GameObject target)
    {
        // After takeover has restored this original widget, the same adapter remains
        // usable on the native desktop. A queued click from a still-floated host is rejected.
        bool desktopPrompt = _panel == null && _presentationEnabled && FlatScreen.ManualScreenActive;
        if ((!Visible && !desktopPrompt) || !ReferenceEquals(target, _click) || !NativePromptStanding
            || !MapRoomDriver.Active || !WorldUIConfig.ConversionActive
            || (FlatScreen.ManualScreenActive && !desktopPrompt)
            || (Singleton<ESCMenu>.IsInitialized && Singleton<ESCMenu>.Instance.IsOpen)
            || _source is not UIGuildmasterConfirmActionPopup popup || PopupConfirm == null
            || PopupBlocker?.GetValue(popup) is not SimpleKeyActionHandlerBlocker blocker || blocker.IsBlock
            || _lastClickFrame == Time.frameCount) return false;
        _lastClickFrame = Time.frameCount;
        PopupConfirm.Invoke(popup, null);
        return true;
    }

    internal static void LateTick() => _grab?.LateSyncHost();

    /// <summary>Restore presentation only; a room toggle does not answer the optional prompt.</summary>
    internal static void Reset()
    {
        RestorePresentation();
        _presentationEnabled = false;
        // Retain the native captured prompt while it is standing, so enabling the room again
        // recovers it without replaying the Show edge or resolving its pending promise.
    }

    private static void RestorePresentation(bool keepHit = false)
    {
        if (!keepHit && _click != null)
        {
            _click.SetActive(false);
            UnityEngine.Object.Destroy(_click);
        }
        if (!keepHit) _click = null;
        _grab?.Destroy(); _grab = null;
        if (_panel != null) CanvasConversion.Release(_panel);
        _panel = null;
    }

    internal static void Replaced(MonoBehaviour presenter)
    {
        if (!ReferenceEquals(_presenter, presenter)) return;
        Reset();
        _presenter = _source = null; _callback = null; _failed = false;
    }
}

internal sealed class RetirementPromptClick : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData) =>
        DispatchGuard.Run("MapRetirementPrompt.NativePress", () => MapRetirementPrompt.TryPressConsole(gameObject));
}

[HarmonyPatch]
internal static class RetirementPromptShownPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(UIGuildmasterConfirmActionButtonPresenter), "ShowCharacterRetiredAction");
        yield return AccessTools.Method(typeof(UIGuildmasterConfirmActionPopupPresenter), "ShowCharacterRetiredAction");
    }
    private static void Prefix(MonoBehaviour __instance) =>
        DispatchGuard.Run("MapRetirementPrompt.Replace", () => MapRetirementPrompt.Replaced(__instance));
    private static void Postfix(MonoBehaviour __instance, Action onConfirmCallback) =>
        DispatchGuard.Run("MapRetirementPrompt.Capture", () => MapRetirementPrompt.Capture(__instance, onConfirmCallback));
}

[HarmonyPatch]
internal static class RetirementPromptReplacedPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (Type presenter in new[] { typeof(UIGuildmasterConfirmActionButtonPresenter), typeof(UIGuildmasterConfirmActionPopupPresenter) })
            foreach (string method in new[] { "HideCharacterRetiredAction", "ShowQuestSelectedAction", "ShowCityEncounterAction", "ClearAll" })
                yield return AccessTools.Method(presenter, method);
    }
    private static void Prefix(MonoBehaviour __instance) =>
        DispatchGuard.Run("MapRetirementPrompt.Replace", () => MapRetirementPrompt.Replaced(__instance));
}
