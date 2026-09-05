using System;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.Net;

/// <summary>
/// The real transport: rides the game's Photon-Bolt "side action" side-channel.
///
/// SEND  → <c>FFSNet.Synchronizer.SendSideAction((GameActionType)Sentinel,
///          new CustomDataToken(payload), canBeUnreliable:true, sendToHostOnly:false,
///          targetPlayerID:SentinelTargetPlayerId, 0, 0, false)</c>.
///          That builds a <c>NetworkActionEvent</c> to <c>GlobalTargets.Others</c>,
///          Unreliable, carrying our bytes inside the already-Bolt-registered
///          <c>CustomDataToken</c> (decompiled Synchronizer.cs:23, NetworkAction.cs,
///          CustomDataToken.cs, NetworkCallbacks.cs:22).
///
/// RECEIVE → Harmony PREFIX on <c>FFSNet.ActionProcessor.ProcessSideAction(GameAction)</c>
///          (decompiled ActionProcessor.cs:175, the single chokepoint every inbound side
///          action passes through — NetworkCallbacks.cs:130-155 routes NetworkActionEvent
///          straight there). When the action carries our sentinel type we consume it and
///          RETURN FALSE, so the vanilla body (which would call
///          <c>GameAction.Execute()</c> and desync on an unknown type — GameAction.cs:1055)
///          never runs on a modded peer. On a non-modded peer the sentinel TargetPlayerID
///          makes vanilla ignore it anyway (ActionProcessor.cs:188) → no desync, ever.
///
/// EVERYTHING here is done through reflection so the mod keeps ZERO compile-time dependency
/// on the Photon-Bolt assemblies (bolt.dll etc.) and no csproj edit is required. If any FFSNet
/// member fails to resolve (unexpected game build) the transport degrades to a safe no-op and
/// logs once — the game keeps running vanilla.
///
/// NOTE ON RELAY: <c>GlobalTargets.Others</c> from a CLIENT reaches the host directly; whether
/// Bolt relays it on to the OTHER clients (client→client with a flat host) is the one netcode
/// unknown — see PLAN.md "Residual risks". Host↔client (the common 2-player VR case where one
/// player hosts) is direct and unaffected.
/// </summary>
internal sealed class FfsNetTransport : INetTransport
{
    /// <summary>The single active transport whose <see cref="RaiseReceived"/> the static
    /// Harmony prefix forwards to. Set on <see cref="Install"/>, cleared on <see cref="Uninstall"/>.</summary>
    private static FfsNetTransport? _active;

    private readonly Harmony _harmony;
    private bool _installed;
    private bool _resolved;
    private bool _degraded;

    // Reusable send-arg array (avoids a per-send allocation for the reflection invoke).
    private readonly object?[] _sendArgs = new object?[8];

    // Resolved FFSNet reflection members (all null until Resolve()).
    private MethodInfo? _sendSideAction;
    private Type? _gameActionTypeEnum;
    private object? _sentinelActionType;         // boxed GameActionType value
    private ConstructorInfo? _customDataCtor;
    private PropertyInfo? _customDataProp;        // CustomDataToken.CustomData -> byte[]
    private PropertyInfo? _gaActionTypeId;        // GameAction.ActionTypeID -> int
    private PropertyInfo? _gaSupplementaryToken;  // GameAction.SupplementaryDataToken -> IProtocolToken
    private PropertyInfo? _gaPlayerId;            // GameAction.PlayerID -> int
    private PropertyInfo? _ffsIsOnline;           // FFSNetwork.IsOnline (static) -> bool
    private PropertyInfo? _playerRegistryMyPlayer;// PlayerRegistry.MyPlayer (static) -> NetworkPlayer
    private PropertyInfo? _networkPlayerId;       // NetworkPlayer.PlayerID -> int

    public FfsNetTransport(Harmony harmony)
    {
        _harmony = harmony ?? throw new ArgumentNullException(nameof(harmony));
    }

    public event Action<int, byte[], int>? PacketReceived;

    public bool IsOnline
    {
        get
        {
            if (_degraded || _ffsIsOnline == null)
                return false;
            try { return _ffsIsOnline.GetValue(null) is true; }
            catch { return false; }
        }
    }

    public int LocalPlayerId
    {
        get
        {
            if (_degraded || _playerRegistryMyPlayer == null || _networkPlayerId == null)
                return 0;
            try
            {
                object? mp = _playerRegistryMyPlayer.GetValue(null);
                if (mp == null) return 0;
                return _networkPlayerId.GetValue(mp) is int id ? id : 0;
            }
            catch { return 0; }
        }
    }

    // ---- install / uninstall ------------------------------------------------------------

    public void Install()
    {
        if (_installed)
            return;
        Resolve();
        if (_degraded)
        {
            VRLog.Warn("Net", "FFSNet transport unavailable (member resolution failed) — VR embodiment sync disabled, game runs normally.");
            return;
        }

        try
        {
            MethodInfo target = AccessTools.Method("FFSNet.ActionProcessor:ProcessSideAction");
            if (target == null)
            {
                _degraded = true;
                VRLog.Warn("Net", "Could not find FFSNet.ActionProcessor.ProcessSideAction — receive hook not installed.");
                return;
            }
            var prefix = new HarmonyMethod(typeof(FfsNetTransport).GetMethod(
                nameof(ReceivePrefix), BindingFlags.Static | BindingFlags.NonPublic));
            _harmony.Patch(target, prefix: prefix);

            // Pre-box the constant SendSideAction args once (only slot [1], the token, changes
            // per send) so the 15 Hz send path allocates nothing but the token byte[] copy.
            _sendArgs[0] = _sentinelActionType;
            _sendArgs[2] = true;   // canBeUnreliable
            _sendArgs[3] = false;  // sendToHostOnly (broadcast to Others)
            _sendArgs[4] = NetProtocol.SentinelTargetPlayerId;
            _sendArgs[5] = 0;
            _sendArgs[6] = 0;
            _sendArgs[7] = false;

            _active = this;
            _installed = true;
            VRLog.Info("Net", "FFSNet transport installed (piggyback SendSideAction / ProcessSideAction prefix).");
        }
        catch (Exception e)
        {
            _degraded = true;
            VRLog.Error("Net", $"Failed to install FFSNet receive hook: {e}");
        }
    }

    public void Uninstall()
    {
        // The shared Harmony instance is unpatched wholesale by Plugin.OnDestroy;
        // just detach our static + flags so nothing fires after shutdown.
        if (_active == this)
            _active = null;
        _installed = false;
    }

    // ---- send ---------------------------------------------------------------------------

    public void Send(byte[] payload, int length)
    {
        if (_degraded || !_installed || _sendSideAction == null || _customDataCtor == null || !IsOnline)
            return;
        try
        {
            // CustomDataToken(byte[] customData, bool compressData=false). The token keeps a
            // reference to the array, so hand it an exact-size copy (the caller's buffer is reused).
            var bytes = new byte[length];
            Buffer.BlockCopy(payload, 0, bytes, 0, length);
            object token = _customDataCtor.Invoke(new object[] { bytes, false });

            // Only the token slot changes per send; the rest were pre-boxed in Install().
            // SendSideAction(GameActionType, IProtocolToken, bool canBeUnreliable,
            //                bool sendToHostOnly, int targetPlayerID, int, int, bool)
            _sendArgs[1] = token;
            _sendSideAction.Invoke(null, _sendArgs);
        }
        catch (Exception e)
        {
            // Never let a transport hiccup bubble into game code.
            VRLog.Error("Net", $"SendSideAction failed (suppressed): {e.Message}");
        }
    }

    // ---- receive hook -------------------------------------------------------------------

    /// <summary>
    /// Harmony prefix on <c>ActionProcessor.ProcessSideAction(GameAction action)</c>.
    /// Returns false (skip vanilla) ONLY for our sentinel packets; true for everything else.
    /// Declared with an <c>object</c> parameter named <c>action</c> so Harmony injects the
    /// real GameAction without this class referencing the type.
    /// </summary>
    private static bool ReceivePrefix(object action)
    {
        FfsNetTransport? self = _active;
        if (self == null || action == null)
            return true;
        try
        {
            if (self._gaActionTypeId?.GetValue(action) is not int typeId || typeId != NetProtocol.SentinelActionTypeId)
                return true; // not ours — let the game process it normally

            // A CONTROL REQUEST rather than a cosmetic packet: a client asking the host to press
            // something on its behalf (EnemyInfoContinue — "any player may end the enemy-info
            // reveal"). It rides the same sentinel type with NO payload token, and is recognised by
            // markers in the NetworkAction's own DataInt/DataInt2/DataBoolean.
            //
            // WHY IT LIVES HERE AND NOT IN A SECOND HARMONY PREFIX. This method already IS the
            // mod's prefix on ActionProcessor.ProcessSideAction, the single chokepoint every
            // inbound side action passes through. A second prefix on the same method would be a
            // duplicate mechanism AND an ordering hazard, because a prefix that returns false
            // suppresses the ones after it. One seam, one explicit order. The request handler takes
            // the raw object so this file keeps its zero-compile-time-dependency stance on the Bolt
            // token types; nothing below this line names one.
            if (EnemyInfoContinue.TryHandleSideAction(action))
                return false;

            // Ours: pull the payload out of the CustomDataToken and hand it up. Consume it.
            object? tokenObj = self._gaSupplementaryToken?.GetValue(action);
            if (tokenObj != null && self._customDataProp?.GetValue(tokenObj) is byte[] bytes && bytes.Length > 0)
            {
                int senderId = self._gaPlayerId?.GetValue(action) is int pid ? pid : 0;
                self.RaiseReceived(senderId, bytes, bytes.Length);
            }
            return false; // never run vanilla ProcessSideAction for our cosmetic packet
        }
        catch (Exception e)
        {
            // Could not classify the action → let vanilla run it. Safe both ways: a real game
            // action MUST reach the game, and one of OUR packets that slipped here still
            // carries the sentinel TargetPlayerID, so vanilla ProcessSideAction ignores it
            // without ever calling Execute() (no desync — ActionProcessor.cs:188).
            VRLog.Error("Net", $"ReceivePrefix error (deferring to vanilla): {e.Message}");
            return true;
        }
    }

    private void RaiseReceived(int senderId, byte[] buffer, int length)
    {
        try { PacketReceived?.Invoke(senderId, buffer, length); }
        catch (Exception e) { VRLog.Error("Net", $"PacketReceived subscriber threw: {e}"); }
    }

    // ---- reflection resolution ----------------------------------------------------------

    private void Resolve()
    {
        if (_resolved)
            return;
        _resolved = true;
        try
        {
            _sendSideAction = AccessTools.Method("FFSNet.Synchronizer:SendSideAction");

            _gameActionTypeEnum = AccessTools.TypeByName("FFSNet.GameActionType");
            if (_gameActionTypeEnum is { IsEnum: true })
                _sentinelActionType = Enum.ToObject(_gameActionTypeEnum, NetProtocol.SentinelActionTypeId);

            Type? customData = AccessTools.TypeByName("FFSNet.CustomDataToken");
            _customDataCtor = customData?.GetConstructor(new[] { typeof(byte[]), typeof(bool) });
            _customDataProp = customData?.GetProperty("CustomData");

            Type? gameAction = AccessTools.TypeByName("FFSNet.GameAction");
            _gaActionTypeId = gameAction?.GetProperty("ActionTypeID");
            _gaSupplementaryToken = gameAction?.GetProperty("SupplementaryDataToken");
            _gaPlayerId = gameAction?.GetProperty("PlayerID");

            Type? ffsNetwork = AccessTools.TypeByName("FFSNetwork");
            _ffsIsOnline = ffsNetwork?.GetProperty("IsOnline", BindingFlags.Public | BindingFlags.Static);

            Type? playerRegistry = AccessTools.TypeByName("FFSNet.PlayerRegistry");
            _playerRegistryMyPlayer = playerRegistry?.GetProperty("MyPlayer", BindingFlags.Public | BindingFlags.Static);

            Type? networkPlayer = AccessTools.TypeByName("FFSNet.NetworkPlayer");
            _networkPlayerId = networkPlayer?.GetProperty("PlayerID");

            _degraded = _sendSideAction == null || _sentinelActionType == null || _customDataCtor == null
                        || _customDataProp == null || _gaActionTypeId == null || _gaSupplementaryToken == null
                        || _gaPlayerId == null || _ffsIsOnline == null || _playerRegistryMyPlayer == null
                        || _networkPlayerId == null;
        }
        catch (Exception e)
        {
            _degraded = true;
            VRLog.Error("Net", $"FFSNet reflection resolution threw: {e}");
        }
    }
}
