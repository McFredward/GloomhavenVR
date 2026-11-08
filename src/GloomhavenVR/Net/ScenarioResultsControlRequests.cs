using System;
using FFSNet;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;

namespace GloomhavenVR.Net;

/// <summary>Explicit visitor presses on the current shared Results picture ask the native host.</summary>
internal static class ScenarioResultsControlRequests
{
    // Paired Build764 reaches the genuine final Results, but its observer picture
    // cannot run native controllers. Reuse the existing reliable host-only request
    // path; the host invokes only its original live pointer callback. The opening
    // occupies all 32 DataInt2 bits, independently of the three DataInt tags. Never
    // pack an operation into that key: a later opening must not accept an old press.
    internal static bool Request(byte operation, uint opening) => Desync.DispatchGuard.Run(
        "ScenarioResultsControlRequests.Request", () => Send(operation, opening), onThrow: false);

    private static bool Send(byte operation, uint opening)
    {
        int tag = Tag(operation);
        if (tag == 0 || !CurrentContext(host: false, out int local)
            || !CurrentOpening(opening) || !NativeCheatQueue.HasCurrentVersion(PlayerRegistry.HostPlayerID))
            return false;
        bool sent = SideActionRequest.Send((GameActionType)NetProtocol.SentinelActionTypeId,
            NetProtocol.SentinelTargetPlayerId, tag, unchecked((int)opening), dataBool: true);
        VRLog.Note("Net", $"RESULTS CONTROL PRESS: player={local}, opening={opening}, operation={operation}, sent={sent}.");
        return sent;
    }

    /// <summary>Consume only our complete marker, including stale/refused owned requests.</summary>
    internal static bool TryHandleSideAction(object? action)
    {
        if (action is not GameAction request || !request.SupplementaryDataBoolean
            || request.ActionTypeID != NetProtocol.SentinelActionTypeId
            || request.TargetPlayerID != NetProtocol.SentinelTargetPlayerId
            || Operation(request.DataInt) == 0) return false;
        Desync.DispatchGuard.Run("ScenarioResultsControlRequests.Apply", () => Apply(request));
        return true;
    }

    private static void Apply(GameAction request)
    {
        uint opening = unchecked((uint)request.DataInt2);
        byte operation = Operation(request.DataInt);
        bool accepted = CurrentContext(host: true, out int local)
            && request.PlayerID > 0 && request.PlayerID != local
            && CurrentOpening(opening) && NativeCheatQueue.HasCurrentVersion(request.PlayerID)
            && ScenarioResultsNative.TryInvokeContinuation(operation);
        // An explicit press is event-driven. No per-frame/native-roster tracing,
        // character ownership requirement, synthetic vote or local input latch.
        // A refused press leaves the original control available for another try.
        VRLog.Note("Net", $"RESULTS CONTROL REQUEST: player={request.PlayerID}, opening={opening}, operation={operation}, accepted={accepted}.");
    }

    private static bool CurrentContext(bool host, out int local)
    {
        local = 0;
        return !NetSession.FlatNetMode && FFSNetwork.IsOnline && !FFSNetwork.HasDesynchronized
            && (host ? FFSNetwork.IsHost && !FFSNetwork.IsClient : FFSNetwork.IsClient && !FFSNetwork.IsHost)
            && NativeCheatQueue.ReadRoster(out _, out local, requireStable: false)
            && (host ? local == PlayerRegistry.HostPlayerID : local != PlayerRegistry.HostPlayerID);
    }

    private static bool CurrentOpening(uint opening) => opening != 0 && opening == ScenarioResultsPose.Key
        && ScenarioResultsPose.Author == PlayerRegistry.HostPlayerID && ScenarioResultsPose.NativeVisible;

    private static int Tag(byte operation) => operation switch
    {
        ScenarioResultsNative.ContinueReady => NetProtocol.SideRequestResultsContinue,
        ScenarioResultsNative.ReturnToMap => NetProtocol.SideRequestResultsReturn,
        ScenarioResultsNative.RetryScenario => NetProtocol.SideRequestResultsRetry,
        _ => 0
    };

    private static byte Operation(int tag) => tag switch
    {
        NetProtocol.SideRequestResultsContinue => ScenarioResultsNative.ContinueReady,
        NetProtocol.SideRequestResultsReturn => ScenarioResultsNative.ReturnToMap,
        NetProtocol.SideRequestResultsRetry => ScenarioResultsNative.RetryScenario,
        _ => 0
    };
}
