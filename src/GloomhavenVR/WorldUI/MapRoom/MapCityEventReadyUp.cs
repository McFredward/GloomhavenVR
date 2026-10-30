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
    private static bool _actionWarned;
    private static bool _reconciling;
    private static bool _clearBrowsingPending;
    // NetworkPlayer inherits Bolt's EntityBehaviour<T>, intentionally not a mod build
    // reference. Read its existing public state interface only at proposal edges.
    private static readonly PropertyInfo? NativeState = ReadNativeStateProperty();
    private static readonly PropertyInfo? NativeActionCounter = ReadNativeActionProperty();

    /// <summary>Only the public preflight currently occupying this original singleton.</summary>
    internal static bool Active
    {
        get
        {
            try { return MapRoomDriver.Active && MatchesNativeProposal(); }
            catch (Exception ex) { ReportFailure("active proposal", ex); return false; }
        }
    }
    internal static UIWindow? Window
    {
        get
        {
            try { return Active ? _toggle!.GetComponent<UIWindow>() : null; }
            catch (Exception ex) { ReportFailure("native window", ex); return null; }
        }
    }
    internal static uint ContentKey => Active ? _contentKey : 0;

    /// <summary>Logical public decision ownership survives a local 2D/3D participation flip.</summary>
    internal static bool OwnsMapDecision
    {
        get
        {
            bool knownEvent = false;
            try { return MatchesNativeRole() || HasLiveEvent(out knownEvent); }
            catch (Exception ex)
            {
                ReportFailure("public map decision", ex);
                return _contentKey != 0 || knownEvent;
            }
        }
    }

    /// <summary>Only user browsing in the replaced 3D map UI is gated here.</summary>
    internal static bool BlocksQuestBrowsing
    {
        get
        {
            try { return MapRoomDriver.Active && OwnsMapDecision; }
            catch (Exception ex) { ReportFailure("quest browsing scope", ex); return OwnsMapDecision; }
        }
    }

    internal static void Install()
    {
        try
        {
            if (_installed || VRSession.Harmony == null)
                return;
            VRSession.Harmony.PatchAll(typeof(CityProposalSeams));
            _installed = true;
        }
        catch (Exception ex) { ReportFailure("patch installation", ex); }
    }

    private static UIReadyToggle? ForgetModLease()
    {
        // Pure CLR field changes only. An error handler must never recursively enter native
        // visibility/game callbacks while trying to revoke a failed presentation lease.
        UIReadyToggle? old = _toggle;
        _controller = null;
        _toggle = null;
        _contentKey = 0;
        _refreshPermission = false;
        _roomWasActive = false;
        _clearBrowsingPending = false;
        return old;
    }

    internal static void Reset()
    {
        // Drop the lease before any native visibility recomputation. Never hide a new quest,
        // loadout, retirement or reward that has already reused this same UIWindow instance.
        UIReadyToggle? old = ForgetModLease();
        try
        {
            if (old != null && old.readyUpToggleState == EReadyUpToggleStates.CityEvents)
                old.UpdateVisiblity();
        }
        catch (Exception ex) { ReportFailure("reset visibility", ex); }
    }

    private static void ReportFailure(string seam, Exception error)
    {
        if (_warned)
            return;
        _warned = true; // Bound retries even if the diagnostic logger itself fails.
        try
        {
            VRLog.Warn("MapRoom", "MAP CITY READY presentation failed — "
                + $"{seam}: {error.GetType().Name}: {error.Message}. Native readiness retained; "
                + "one report per process.");
        }
        catch (Exception) { }
    }

    private static PropertyInfo? ReadNativeStateProperty()
    {
        try { return AccessTools.Property(typeof(NetworkPlayer), "state"); }
        catch (Exception ex) { ReportFailure("native state metadata", ex); return null; }
    }

    private static PropertyInfo? ReadNativeActionProperty()
    {
        try { return NativeState?.PropertyType.GetProperty("LatestProcessedActionID"); }
        catch (Exception ex) { ReportFailure("native action metadata", ex); return null; }
    }

    /// <summary>Tick outside the map-room-active branch too, so observation retires on exit.</summary>
    internal static void Tick()
    {
        if (_contentKey == 0 || _reconciling)
            return;
        _reconciling = true;
        try
        {
            if (!MatchesNativeRole())
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
            if (!_roomWasActive)
                _clearBrowsingPending = true;
            _roomWasActive = true;
            UIReadyToggle current = _toggle!;
            uint key = _contentKey;
            bool clearedBrowsing = TryClearUnacceptedBrowsing();
            if (_contentKey != key || !ReferenceEquals(_toggle, current))
                return;
            if (current._allPlayersReady)
            {
                // The native ACK wait hides input but has not withdrawn the city proposal.
                // Keep its role protected until the original Reset/StartEvent/cancel edge.
                if (clearedBrowsing)
                    RefreshCityPresentation();
                else
                    current.UpdateVisiblity();
                return;
            }
            // ToggleReadyUpUI is a native presentation request, not OnSelectedCityEvent:
            // the latter calls ReadyUp(true). A client's local unready hides its preview,
            // but does not withdraw the host's offer. Keep that original offer observable.
            if (!_toggle!._requestVisible || _controller!.m_State != EReadyUpToggleStates.CityEvents || clearedBrowsing)
                RefreshCityPresentation();
            if (_contentKey != key || !ReferenceEquals(_toggle, current))
                return; // Native show callbacks may legitimately retire or replace the offer.
            if (_refreshPermission && Singleton<MapChoreographer>.IsInitialized)
            {
                Singleton<MapChoreographer>.Instance.DeterminePlayerToggleInteractability();
                if (_contentKey != key || !ReferenceEquals(_toggle, current))
                    return; // Native host permission loss can cancel the original city offer.
                _refreshPermission = false;
            }
            current.UpdateVisiblity();
        }
        catch (Exception ex)
        {
            // A failed read is unknown, not a measured cancellation. Retain the exact lease
            // for a safe retry; importantly, input still knows which observer it must protect.
            ReportFailure("presentation tick", ex);
        }
        finally
        {
            _reconciling = false;
        }
    }

    private static bool MatchesNativeProposal() => MatchesNativeRole() && !_toggle!._allPlayersReady;

    private static void RefreshCityPresentation()
    {
        _controller!.ToggleReadyUpUI(true, EReadyUpToggleStates.CityEvents);
    }

    private static bool HasLiveEvent(out bool knownContent)
    {
        knownContent = false;
        if (!Singleton<UIEventPanel>.IsInitialized)
            return false;
        UIEventPanel panel = Singleton<UIEventPanel>.Instance;
        if (panel == null || panel.eventData == null)
            return false;
        knownContent = true;
        // Native OnFinishedEvent hides this window BEFORE the deferred applied-effects
        // and reward distribution callbacks. OnDistributedRewards clears eventData at
        // the actual completion boundary; window fade is never gameplay completion.
        return true;
    }

    private static bool TryClearUnacceptedBrowsing()
    {
        // CancelTravel is the original presentation cleanup, not its user button callback:
        // the latter enters WorldMap navigation. Never steal focus from a live encounter.
        if (!_clearBrowsingPending || HasLiveEvent(out _)
            || !Singleton<AdventureMapUIManager>.IsInitialized || _controller == null)
            return false;
        AdventureMapUIManager map = Singleton<AdventureMapUIManager>.Instance;
        if (map == null || _controller.HostSelectedQuest != null || map.IsLocked)
            return false;
        MapLocation? browsing = map.LocationToTravel;
        _clearBrowsingPending = false; // At most one native cancel attempt per capture/reentry.
        if (browsing == null)
            return false;
        map.CancelTravel(); // Native CanCancelSelectedLocation still owns the final permission.
        // HideTravelWarning also hid the native city help text. Refresh the original city
        // presentation once only when cleanup actually removed this unaccepted browse.
        return map.LocationToTravel == null;
    }

    private static bool MatchesNativeRole()
    {
        if (_contentKey == 0 || !FFSNetwork.IsOnline || _controller == null || _toggle == null
            || !Singleton<UIMapMultiplayerController>.IsInitialized
            || !Singleton<UIReadyToggle>.IsInitialized
            || !ReferenceEquals(_controller, Singleton<UIMapMultiplayerController>.Instance)
            || !ReferenceEquals(_toggle, Singleton<UIReadyToggle>.Instance)
            || _toggle.readyUpToggleState != EReadyUpToggleStates.CityEvents)
            return false;
        var map = AdventureState.MapState;
        return map != null && (map.CurrentMapPhaseType == EMapPhaseType.InHQ
            || map.CurrentMapPhaseType == EMapPhaseType.AtLinkedScenario);
    }

    private static void Capture(int actionId)
    {
        try { CaptureCore(actionId); }
        catch (Exception ex) { ReportFailure("native proposal capture", ex); }
    }

    private static void CaptureCore(int actionId)
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
        _clearBrowsingPending = true;
        VRLog.Info("MapRoom", $"MAP CITY READY proposal — action={actionId}, key={_contentKey}; "
            + "native votes and continuation unchanged.");
    }

    // Disabled participants must still see the common offer, without gaining a vote. Preserve
    // native visibility blockers/all-ready and the actual toggle.interactable bit. The paired
    // InputToggle prefix also protects the native gamepad path, which otherwise checks only
    // IsVisible rather than _interactable. It never changes consent or native permission.
    private static bool IsCityObserver(UIReadyToggle toggle) => _contentKey != 0
        && ReferenceEquals(_toggle, toggle) && MapRoomDriver.Active && MatchesNativeProposal();

    private static int NativeActionId()
    {
        try
        {
            if (ReferenceEquals(PlayerRegistry.MyPlayer, null) || NativeState == null || NativeActionCounter == null)
                return -1;
            object? state = NativeState.GetValue(PlayerRegistry.MyPlayer);
            return state != null && NativeActionCounter.GetValue(state) is int id ? id : -1;
        }
        catch (Exception ex)
        {
            // Unknown native provenance must not accidentally reuse a stale shared opening.
            ReportFailure("native action counter", ex);
            return -1;
        }
    }

    [HarmonyPatch]
    private static class CityProposalSeams
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIMapMultiplayerController), nameof(UIMapMultiplayerController.ConfirmCityEvent))]
        private static void BeforeHostProposal(out int __state)
        {
            __state = -1;
            try
            {
                if (FFSNetwork.IsOnline && FFSNetwork.IsHost)
                    __state = NativeActionId();
            }
            catch (Exception ex) { ReportFailure("host proposal prefix", ex); }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIMapMultiplayerController), nameof(UIMapMultiplayerController.ConfirmCityEvent))]
        private static void AfterHostProposal(int __state)
        {
            try
            {
                // ConfirmCityEvent constructs exactly SelectCityEvent before ReadyUpPlayer's
                // caller constructs its following action. Refuse an ambiguous action sequence.
                if (!FFSNetwork.IsOnline || !FFSNetwork.IsHost
                    || !Singleton<UIMapMultiplayerController>.IsInitialized
                    || !Singleton<UIReadyToggle>.IsInitialized)
                    return;
                UIMapMultiplayerController controller = Singleton<UIMapMultiplayerController>.Instance;
                UIReadyToggle toggle = Singleton<UIReadyToggle>.Instance;
                var map = AdventureState.MapState;
                if (controller == null || toggle == null || map == null
                    || controller.m_State != EReadyUpToggleStates.CityEvents
                    || toggle.readyUpToggleState != EReadyUpToggleStates.CityEvents
                    || (map.CurrentMapPhaseType != EMapPhaseType.InHQ
                        && map.CurrentMapPhaseType != EMapPhaseType.AtLinkedScenario))
                    return;
                int current = NativeActionId();
                if (__state >= 0 && current == __state + 1)
                {
                    Capture(current);
                    return;
                }
                if (_actionWarned)
                    return;
                _actionWarned = true;
                try { VRLog.Warn("MapRoom", "MAP CITY READY host occurrence unavailable — "
                    + $"native action counter before={__state}, after={current}. Native city proposal retained; "
                    + "shared placement cannot use ambiguous provenance. One report per process."); }
                catch (Exception) { }
            }
            catch (Exception ex) { ReportFailure("host proposal postfix", ex); }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapChoreographer), nameof(MapChoreographer.ProxySelectedCityEvent))]
        private static void AfterClientProposal(GameAction action)
        {
            try
            {
                if (FFSNetwork.IsClient && action != null
                    && action.ActionTypeID == (int)GameActionType.SelectCityEvent)
                    Capture(action.ActionID);
            }
            catch (Exception ex) { ReportFailure("client proposal postfix", ex); }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIReadyToggle), nameof(UIReadyToggle.Initialize))]
        private static void BeforeReadyInitialize(ref bool validateReadyUpOnPlayerLeft,
                                                 UIReadyToggle.EReadyUpType readyUpType,
                                                 EReadyUpToggleStates readyUpToggleState)
        {
            bool preserveCity = false;
            try
            {
                // The native 2D city-confirm callback reinitializes the SAME CityEvents
                // proposal before its own explicit ReadyUp(true). That does not send a new
                // SelectCityEvent on clients. Retain only an already measured exact lease;
                // never synthesize an occurrence or invoke that consent callback ourselves.
                preserveCity = VRSession.IsRunning
                    && readyUpType == UIReadyToggle.EReadyUpType.Participant
                    && readyUpToggleState == EReadyUpToggleStates.CityEvents
                    && MatchesNativeRole();
            }
            catch (Exception ex) { ReportFailure("city initialization identity", ex); }
            if (!preserveCity)
                Reset();
            else
                _refreshPermission = true;
            // City initialization disables the same original departure validation as quests.
            // With host+one peer ready, an unready third participant leaving otherwise returns
            // from native OnPlayerLeft before the now-sufficient quorum starts its ACK path.
            // Enable that existing native path, rather than manufacturing votes or proceeding.
            try
            {
                if (!VRSession.IsRunning || !FFSNetwork.IsOnline
                    || readyUpType != UIReadyToggle.EReadyUpType.Participant
                    || readyUpToggleState != EReadyUpToggleStates.CityEvents)
                    return;
                ActionPhaseType phase = ActionProcessor.CurrentPhase;
                if (phase == ActionPhaseType.MapHQ || phase == ActionPhaseType.MapAtLinkedScenario)
                    validateReadyUpOnPlayerLeft = true;
            }
            catch (Exception ex)
            {
                // Missing native phase evidence cannot authorize a change to the caller's
                // option, nor may a diagnostic abort the original native initialization.
                ReportFailure("MAP CITY READY departure probe failed", ex);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIReadyToggle), nameof(UIReadyToggle.Reset))]
        private static void BeforeReadyReset() => Reset();

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIMapMultiplayerController), nameof(UIMapMultiplayerController.ResetUI))]
        private static void BeforeControllerReset() => Reset();

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIMapMultiplayerController), nameof(UIMapMultiplayerController.ClearHostSelectedCityEvent))]
        private static void BeforeHostWithdrawal() => Reset();

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIMapMultiplayerController), "ProxyHostCancelledCityEvent")]
        private static void BeforeHostCancellation() => Reset();

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIEventPanel), nameof(UIEventPanel.StartEvent))]
        private static void BeforeEventCard() => Reset();

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIMapMultiplayerController), "CancelPreviewCityEvent")]
        private static void BeforeCancelPreview()
        {
            try
            {
                if (FFSNetwork.IsHost)
                    Reset();
            }
            catch (Exception ex)
            {
                // A failed host/client probe must not resurrect a natively cancelled offer.
                ForgetModLease();
                ReportFailure("native cancel preview", ex);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIMapMultiplayerController), nameof(UIMapMultiplayerController.ToggleReadyUpUI))]
        private static bool PreserveCityRole(UIMapMultiplayerController __instance, EReadyUpToggleStates state)
        {
            // AdventureMapUIManager.EnableTravelOptions unconditionally requests Quests on
            // an online host, including party-panel focus refreshes unrelated to selection.
            // This mutable controller presentation field is not a native city withdrawal.
            // A genuine quest initialization retires this lease first and remains unchanged.
            if (state != EReadyUpToggleStates.Quests || _contentKey == 0
                || !ReferenceEquals(_controller, __instance))
                return true;
            try
            {
                if (!MapRoomDriver.Active || _toggle == null
                    || _toggle.readyUpToggleState != EReadyUpToggleStates.CityEvents)
                    return true;
                return !MatchesNativeRole();
            }
            catch (Exception ex)
            {
                ReportFailure("city role presentation", ex);
                return false; // Unknown evidence cannot let a focus refresh steal this role.
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(UIReadyToggle), nameof(UIReadyToggle.ShouldBeVisible), MethodType.Getter)]
        private static void ObserveDisabledCityOffer(UIReadyToggle __instance, ref bool __result)
        {
            try
            {
                if (!__result && IsCityObserver(__instance))
                    __result = __instance._requestVisible && !__instance._allPlayersReady
                        && __instance._visibilityRequests.Count == 0;
            }
            catch (Exception ex)
            {
                // Keep the result the original native getter already established. An
                // unavailable observation is not permission to force visibility either way.
                ReportFailure("observer visibility", ex);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(UIReadyToggle), "InputToggle")]
        private static bool PreserveNativeInputPermission(UIReadyToggle __instance)
        {
            bool leased = _contentKey != 0 && ReferenceEquals(_toggle, __instance);
            if (!leased)
                return true;
            try
            {
                if (__instance.readyUpToggleState != EReadyUpToggleStates.CityEvents || !MapRoomDriver.Active)
                    return true;
                return MatchesNativeProposal() && __instance.IsInteractable && __instance.ShouldBeVisible;
            }
            catch (Exception ex)
            {
                // Do not convert failed exact-city identity evidence into "not ours" and
                // admit the original gamepad path, which has no interactability check.
                ReportFailure("observer input permission", ex);
                return false;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(QuestManager), "OnSelectedLogQuest")]
        private static bool BeforeQuestLogBrowse() => !BlocksQuestBrowsing;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MapLocation), nameof(MapLocation.OnPointerClick))]
        private static bool BeforeMapLocationBrowse() => !BlocksQuestBrowsing;
    }
}
