using System;
using System.Globalization;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using HarmonyLib;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.PhaseManager;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// Presentation of the exact native city-event proposal, before its all-ready event card.
/// The desktop proposal presenter has an empty ShowCityEncounterAction implementation.
/// Calling its supplied continuation would enter OpenCityEncounter and automatically ready
/// this player, unlike the quest's preview-only continuation. Reveal the already initialized
/// native toggle instead; its original input, individual vote and all-player ACK remain native.
/// </summary>
internal static class MapCityEventReadyUp
{
    private static UIMapMultiplayerController? _controller;
    private static UIReadyToggle? _toggle;
    private static uint _contentKey;
    private static bool _refreshPermission;
    private static bool _roomWasActive;
    private static bool _installed;
    private static bool _warned;
    private static bool _reconciling;
    // NetworkPlayer inherits Bolt's EntityBehaviour<T>, intentionally not a mod build
    // reference. Read its existing public state interface only at proposal edges.
    private static readonly PropertyInfo? NativeState = AccessTools.Property(typeof(NetworkPlayer), "state");
    private static readonly PropertyInfo? NativeActionCounter = NativeState?.PropertyType.GetProperty("LatestProcessedActionID");

    /// <summary>Only the public preflight currently occupying this original singleton.</summary>
    internal static bool Active => MapRoomDriver.Active && MatchesNativeProposal();
    internal static UIWindow? Window => Active ? _toggle!.GetComponent<UIWindow>() : null;
    internal static uint ContentKey => Active ? _contentKey : 0;

    internal static void Install()
    {
        if (_installed || VRSession.Harmony == null)
            return;
        VRSession.Harmony.PatchAll(typeof(CityProposalSeams));
        _installed = true;
    }

    internal static void Reset()
    {
        // Drop the lease before any native visibility recomputation. Never hide a new quest,
        // loadout, retirement or reward that has already reused this same UIWindow instance.
        UIReadyToggle? old = _toggle;
        bool city = old != null && old.readyUpToggleState == EReadyUpToggleStates.CityEvents;
        _controller = null;
        _toggle = null;
        _contentKey = 0;
        _refreshPermission = false;
        _roomWasActive = false;
        if (city)
            old!.UpdateVisiblity();
    }

    /// <summary>Tick outside the map-room-active branch too, so observation retires on exit.</summary>
    internal static void Tick()
    {
        if (_contentKey == 0 || _reconciling)
            return;
        if (!MatchesNativeProposal())
        {
            Reset();
            return;
        }
        bool roomActive = MapRoomDriver.Active;
        if (!roomActive)
        {
            if (_roomWasActive)
                _toggle!.UpdateVisiblity();
            _roomWasActive = false;
            return;
        }
        _roomWasActive = true;
        _reconciling = true;
        try
        {
            // ToggleReadyUpUI is a native presentation request, not OnSelectedCityEvent:
            // the latter calls ReadyUp(true). A client's local unready hides its preview,
            // but does not withdraw the host's offer. Keep that original offer observable.
            if (!_toggle!._requestVisible)
                _controller!.ToggleReadyUpUI(true, EReadyUpToggleStates.CityEvents);
            if (_refreshPermission && Singleton<MapChoreographer>.IsInitialized)
            {
                _refreshPermission = false;
                Singleton<MapChoreographer>.Instance.DeterminePlayerToggleInteractability();
            }
            _toggle.UpdateVisiblity();
        }
        catch (Exception ex)
        {
            if (!_warned)
            {
                _warned = true;
                VRLog.Warn("MapRoom", "MAP CITY READY presentation failed — "
                    + $"{ex.GetType().Name}: {ex.Message}. Native readiness remains authoritative.");
            }
        }
        finally
        {
            _reconciling = false;
        }
    }

    private static bool MatchesNativeProposal()
    {
        if (_contentKey == 0 || !FFSNetwork.IsOnline || _controller == null || _toggle == null
            || !Singleton<UIMapMultiplayerController>.IsInitialized
            || !Singleton<UIReadyToggle>.IsInitialized
            || !ReferenceEquals(_controller, Singleton<UIMapMultiplayerController>.Instance)
            || !ReferenceEquals(_toggle, Singleton<UIReadyToggle>.Instance)
            || _controller.m_State != EReadyUpToggleStates.CityEvents
            || _toggle.readyUpToggleState != EReadyUpToggleStates.CityEvents
            || _toggle._allPlayersReady)
            return false;
        var map = AdventureState.MapState;
        return map != null && (map.CurrentMapPhaseType == EMapPhaseType.InHQ
            || map.CurrentMapPhaseType == EMapPhaseType.AtLinkedScenario);
    }

    private static void Capture(int actionId)
    {
        if (actionId <= 0 || !FFSNetwork.IsOnline
            || !Singleton<UIMapMultiplayerController>.IsInitialized
            || !Singleton<UIReadyToggle>.IsInitialized)
            return;
        UIMapMultiplayerController controller = Singleton<UIMapMultiplayerController>.Instance;
        UIReadyToggle toggle = Singleton<UIReadyToggle>.Instance;
        if (controller == null || toggle == null || controller.m_State != EReadyUpToggleStates.CityEvents
            || toggle.readyUpToggleState != EReadyUpToggleStates.CityEvents)
            return;
        uint hash = SharedMapRunIdentity.Add(SharedMapRunIdentity.Key, "city-event-ready");
        hash = SharedMapRunIdentity.Add(hash, actionId.ToString(CultureInfo.InvariantCulture));
        _controller = controller;
        _toggle = toggle;
        _contentKey = hash == 0 ? 1u : hash;
        _refreshPermission = true;
        _roomWasActive = false;
        VRLog.Info("MapRoom", $"MAP CITY READY proposal — action={actionId}, key={_contentKey}; "
            + "native votes and continuation unchanged.");
    }

    // Disabled participants must still see the common offer, without gaining a vote. Preserve
    // native visibility blockers/all-ready and the actual toggle.interactable bit. The paired
    // InputToggle prefix also protects the native gamepad path, which otherwise checks only
    // IsVisible rather than _interactable. It never changes consent or native permission.
    private static bool IsCityObserver(UIReadyToggle toggle) => Active && ReferenceEquals(_toggle, toggle);

    private static int NativeActionId()
    {
        if (ReferenceEquals(PlayerRegistry.MyPlayer, null) || NativeState == null || NativeActionCounter == null)
            return -1;
        try
        {
            object? state = NativeState.GetValue(PlayerRegistry.MyPlayer);
            return state != null && NativeActionCounter.GetValue(state) is int id ? id : -1;
        }
        catch (Exception)
        {
            // Unknown native provenance must not accidentally reuse a stale shared opening.
            return -1;
        }
    }

    [HarmonyPatch]
    private static class CityProposalSeams
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIMapMultiplayerController), nameof(UIMapMultiplayerController.ConfirmCityEvent))]
        private static void BeforeHostProposal(out int __state) =>
            __state = FFSNetwork.IsOnline && FFSNetwork.IsHost ? NativeActionId() : -1;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIMapMultiplayerController), nameof(UIMapMultiplayerController.ConfirmCityEvent))]
        private static void AfterHostProposal(int __state)
        {
            // ConfirmCityEvent constructs exactly SelectCityEvent before ReadyUpPlayer's
            // caller constructs its following action. Refuse an ambiguous action sequence.
            if (__state >= 0 && FFSNetwork.IsHost && NativeActionId() == __state + 1)
                Capture(__state + 1);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapChoreographer), nameof(MapChoreographer.ProxySelectedCityEvent))]
        private static void AfterClientProposal(GameAction action)
        {
            if (FFSNetwork.IsClient && action != null
                && action.ActionTypeID == (int)GameActionType.SelectCityEvent)
                Capture(action.ActionID);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIReadyToggle), nameof(UIReadyToggle.Initialize))]
        [HarmonyPatch(typeof(UIReadyToggle), nameof(UIReadyToggle.Reset))]
        [HarmonyPatch(typeof(UIMapMultiplayerController), nameof(UIMapMultiplayerController.ResetUI))]
        [HarmonyPatch(typeof(UIMapMultiplayerController), nameof(UIMapMultiplayerController.ClearHostSelectedCityEvent))]
        [HarmonyPatch(typeof(UIMapMultiplayerController), "ProxyHostCancelledCityEvent")]
        [HarmonyPatch(typeof(UIEventPanel), nameof(UIEventPanel.StartEvent))]
        private static void BeforeNativeRetirement() => Reset();

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIMapMultiplayerController), "CancelPreviewCityEvent")]
        private static void BeforeCancelPreview()
        {
            if (FFSNetwork.IsHost)
                Reset();
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIReadyToggle), nameof(UIReadyToggle.ShouldBeVisible), MethodType.Getter)]
        private static void ObserveDisabledCityOffer(UIReadyToggle __instance, ref bool __result)
        {
            if (!__result && IsCityObserver(__instance))
                __result = __instance._requestVisible && !__instance._allPlayersReady
                    && __instance._visibilityRequests.Count == 0;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIReadyToggle), "InputToggle")]
        private static bool PreserveNativeInputPermission(UIReadyToggle __instance) =>
            !IsCityObserver(__instance) || (__instance.IsInteractable && __instance.ShouldBeVisible);
    }
}
