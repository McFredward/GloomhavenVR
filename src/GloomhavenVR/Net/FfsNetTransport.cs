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
///          The same prefix also consumes the mod's three CONTROL REQUESTS (a client asking
///          the host to press something on its behalf). Two ride the sentinel; the encounter
///          one rides the game's real <c>GameActionType.ContinueRoadEvent</c> and so is
///          recognised BEFORE the sentinel test — see the branch order in ReceivePrefix.
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
    private readonly ExtrasFragments _fragments = new();
    private readonly ExtrasFragments _animationFragments = new(NetProtocol.MsgUseBarAnimation,
        NetProtocol.MsgUseBarAnimationFragments, assemblyLifetime: ExtrasFragments.PresentationAssemblyLifetime);
    private readonly ExtrasFragments _plumeFragments = new(NetProtocol.MsgCardPlume,
        NetProtocol.MsgCardPlumeFragments, CardPlumeCodec.MaxSize, ExtrasFragments.PresentationAssemblyLifetime);
    private readonly ExtrasFragments _appearanceFragments = new(NetProtocol.MsgCardAppearance,
        NetProtocol.MsgCardAppearanceFragments, CardAppearanceCodec.MaxSize, ExtrasFragments.PresentationAssemblyLifetime);
    private readonly ExtrasFragments _boardFragments = new(NetProtocol.MsgNativeBoard,
        NetProtocol.MsgNativeBoardFragments, NativeBoardCodec.MaxSize, ExtrasFragments.PresentationAssemblyLifetime);
    private readonly ExtrasFragments[] _nativeFragments = CreateNativeFragments();
    private static ExtrasFragments[] CreateNativeFragments()
    {
        var result = new ExtrasFragments[32];
        for (int i = 8; i < result.Length; i++) result[i] = new ExtrasFragments(
            NetProtocol.MsgNativeUseBar, NetProtocol.MsgNativeUseBarFragments, NativeUseBarPacket.MaxSize,
            ExtrasFragments.PresentationAssemblyLifetime);
        return result;
    }
    private readonly ExtrasSendScheduler _extrasQueue = new((ulong)DateTime.UtcNow.Ticks,
        NetProtocol.MsgUseBarAnimation, NetProtocol.MsgUseBarAnimationFragments);
    private byte[]? _versionAnnouncement;
    private double _nextVersionAnnouncement;
    private double _nextFragmentReport;
    private int _sentFragments, _receivedFragments, _completedSnapshots, _fragmentBytes;

    internal void ForgetPeer(int senderId)
    {
        _fragments.Forget(senderId);
        _animationFragments.Forget(senderId);
        _plumeFragments.Forget(senderId);
        _boardFragments.Forget(senderId);
        _appearanceFragments.Forget(senderId);
        for (int i = 8; i < _nativeFragments.Length; i++) _nativeFragments[i].Forget(senderId);
    }
    internal void ResetFragments()
    {
        _fragments.Clear();
        _animationFragments.Clear();
        _plumeFragments.Clear();
        _boardFragments.Clear();
        _appearanceFragments.Clear();
        for (int i = 8; i < _nativeFragments.Length; i++) _nativeFragments[i].Clear();
        _extrasQueue.Clear();
        _nextVersionAnnouncement = 0;
        _nextFragmentReport = 0;
        _sentFragments = _receivedFragments = _completedSnapshots = _fragmentBytes = 0;
    }

    internal void TickFragments(float now)
    {
        if (_degraded || !_installed || NetSession.FlatNetMode || !IsOnline) return;
        try
        {
            if (now >= _nextVersionAnnouncement)
            {
                _versionAnnouncement ??= ExtrasVersionAnnouncement.Write(NetProtocol.ModBuild,
                    MyPluginInfo.PLUGIN_VERSION);
            }
            // The handshake shares the same event budget; it must not accompany a fragment
            // in a catch-up burst. Presence and native motion retain independent snapshots.
            byte[]? page = _extrasQueue.NextBatch(now,
                now >= _nextVersionAnnouncement ? _versionAnnouncement : null);
            if (page != null)
            {
                SendToken(page);
                if (ReferenceEquals(page, _versionAnnouncement))
                    _nextVersionAnnouncement = now + 1;
                _sentFragments++;
                _fragmentBytes += page.Length;
            }
            if (now >= _nextFragmentReport)
            {
                _nextFragmentReport = now + 10;
                VRLog.Info("Net", $"EXTRAS TRANSPORT: sent {_sentFragments} bounded event(s), " +
                    $"{_fragmentBytes} payload bytes; received {_receivedFragments} fragment event(s), " +
                    $"committed {_completedSnapshots} complete snapshot(s); cap " +
                    $"{ExtrasFragments.MaxDatagramBytes} B/event, one event per 50 ms, no catch-up burst.");
                _sentFragments = _receivedFragments = _completedSnapshots = _fragmentBytes = 0;
            }
        }
        catch (Exception e) { VRLog.Error("Net", $"Extras transport tick failed (suppressed): {e.Message}"); }
    }

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
        ResetFragments();
    }

    // ---- send ---------------------------------------------------------------------------

    public void Send(byte[] payload, int length)
    {
        if (_degraded || !_installed || _sendSideAction == null || _customDataCtor == null || !IsOnline)
            return;
        try
        {
            if (length < 6 || length > payload.Length) return;
            int type = NetPacket.PeekType(payload, length);
            if (type == NetProtocol.MsgExtras || type == NetProtocol.MsgUseBarAnimation || type == NetProtocol.MsgCardPlume || type == NetProtocol.MsgNativeBoard || type == NetProtocol.MsgCardAppearance)
            {
                _extrasQueue.Enqueue(payload, length);
                return;
            }
            if (type == NetProtocol.MsgNativeUseBar)
            {
                if (NativeUseBarPacket.TryRead(payload, length, out NativeUseBarSnapshot? native))
                    _extrasQueue.Enqueue(payload, length, native!.Address, native);
                return;
            }
            // CustomDataToken(byte[] customData, bool compressData=false). The token keeps a
            // reference to the array, so hand it an exact-size copy (the caller's buffer is reused).
            var bytes = new byte[length];
            Buffer.BlockCopy(payload, 0, bytes, 0, length);
            SendToken(bytes);
        }
        catch (Exception e)
        {
            // Never let a transport hiccup bubble into game code.
            VRLog.Error("Net", $"SendSideAction failed (suppressed): {e.Message}");
        }
    }

    private void SendToken(byte[] bytes)
    {
        object token = _customDataCtor!.Invoke(new object[] { bytes, false });
        _sendArgs[1] = token;
        _sendSideAction!.Invoke(null, _sendArgs);
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
            // THE THIRD CONTROL REQUEST, AND THE ONLY ONE THAT RIDES A REAL GameActionType — so it
            // has to be recognised BEFORE the sentinel test below, which would hand it straight to
            // the game ("not ours"). It is the encounter option press (EncounterChoice — "any
            // player may answer a city/road encounter"), which travels as the game's own
            // GameActionType.ContinueRoadEvent because that is the only type the host's
            // Halted @ MapEvent will let through, and it is told apart from a vanilla arrival by
            // three terms of its own (TargetPhaseID 0, which the game's three senders never send;
            // the boolean marker; a non-zero screen stamp).
            //
            // WHY IT MUST BE CONSUMED HERE AND NOT JUDGED IN ITS OWN PREFIX FURTHER DOWN. The
            // game's dispatch entry for that type is
            // `Singleton<UIEventPanel>.Instance.ClientContinueRoadEvent(a)` with NO null test
            // (GameAction.cs:189-193), and UIEventPanel is a plain Singleton<T> with no
            // DontDestroyOnLoad — null the moment the host leaves the campaign-map scene. The NRE
            // is thrown at the CALL SITE, so the mod's prefix on ClientContinueRoadEvent (and the
            // "there is no UIEventPanel on this machine" refusal inside it) never runs, and
            // ProcessSideAction's own `catch { HandleDesync(ex); throw; }` (ActionProcessor.cs:194)
            // takes the host's whole session down with it (FFSNetwork.cs:80-83 → Shutdown). Making
            // this branch return false means Execute() — and with it that deref — never runs.
            if (EncounterChoice.TryHandleSideAction(action))
                return false;

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
            //
            // FLAT-NET MODE IS HONOURED HERE, AND FOR THIS ONE REQUEST ONLY AT THIS SEAM.
            // "Als Flat-Spieler joinen" turns every mod net path off (NetSession.cs:11-23); the
            // cosmetic paths have always had the term (TickSend / TickExtrasSend /
            // OnPacketReceived) and the control requests never did, so a flat-mode host still
            // pressed its own buttons on a peer's request. The other two requests carry the term
            // INSIDE their own judgement, where it becomes a named REFUSED line; this one is
            // gated at the call site instead because EnemyInfoContinue.cs is outside this lane.
            // Declining to recognise it is safe here and ONLY here-shaped for a sentinel-typed
            // request: it falls through to vanilla, which ignores it without calling Execute()
            // because SentinelTargetPlayerId is no player's id (ActionProcessor.cs:188). The
            // encounter branch above must never be gated this way — its action type is REAL.
            if (!NetSession.FlatNetMode && EnemyInfoContinue.TryHandleSideAction(action))
                return false;

            // The SECOND control request, and it is the same branch rather than a second prefix for
            // the same reason: "any player may operate the loot/gold ASSIGNMENT window"
            // (AssignmentChoice — user request item 3, 2026-09-07). It is told apart from the one
            // above by its own NetProtocol.SideRequest* tag in DataInt, so the order of these two
            // lines is not load-bearing; each recognises only its own tag and returns false for
            // everything else, this transport's own rig/extras packets included.
            if (AssignmentChoice.TryHandleSideAction(action))
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
        try
        {
            if (NetSession.FlatNetMode || senderId == LocalPlayerId) return;
            if (NetPacket.PeekType(buffer, length) == NetProtocol.MsgPresentationBatch)
            {
                if (PresentationBatch.TryRead(buffer, length, out byte[][]? pages))
                    foreach (byte[] page in pages!) RaiseReceived(senderId, page, page.Length);
                return;
            }
            if (ExtrasVersionAnnouncement.TryRead(buffer, length, out PresenceState version))
            {
                VersionGuard.NotePacket(senderId);
                VersionGuard.NoteExtras(senderId, in version);
                return;
            }
            int type = length >= 6 && length <= buffer.Length ? NetPacket.PeekType(buffer, length) : -1;
            if (type == NetProtocol.MsgExtrasFragments || type == NetProtocol.MsgUseBarAnimationFragments
                || type == NetProtocol.MsgCardPlumeFragments || type == NetProtocol.MsgNativeUseBarFragments
                || type == NetProtocol.MsgNativeBoardFragments || type == NetProtocol.MsgCardAppearanceFragments)
            {
                _receivedFragments++;
                int slot = type == NetProtocol.MsgNativeUseBarFragments ? ExtrasFragments.NativeSlotStream(buffer, length) : -1;
                if (type == NetProtocol.MsgNativeUseBarFragments && (slot < 8 || slot >= 32)) return;
                ExtrasFragments assembler = type == NetProtocol.MsgExtrasFragments ? _fragments
                    : type == NetProtocol.MsgUseBarAnimationFragments ? _animationFragments
                    : type == NetProtocol.MsgCardPlumeFragments ? _plumeFragments
                    : type == NetProtocol.MsgNativeBoardFragments ? _boardFragments
                    : type == NetProtocol.MsgCardAppearanceFragments ? _appearanceFragments : _nativeFragments[slot];
                byte[]? complete = assembler.Accept(senderId, buffer, length,
                    System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency);
                if (complete != null)
                {
                    if (slot >= 8 && (!NativeUseBarPacket.TryRead(complete, complete.Length, out NativeUseBarSnapshot? native)
                        || native!.Address != slot)) return;
                    _completedSnapshots++;
                    PacketReceived?.Invoke(senderId, complete, complete.Length);
                }
                return;
            }
            PacketReceived?.Invoke(senderId, buffer, length);
        }
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
