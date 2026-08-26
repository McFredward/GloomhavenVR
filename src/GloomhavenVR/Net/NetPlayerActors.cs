using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// REFLECTION-ONLY bridge from an FFSNet player id to the NON-networked game objects we may
/// safely touch: the player's <see cref="CPlayerActor"/>, their Steam avatar sprite, and their
/// (bad-word-masked) username, plus the local player's stable index for the spawn circle.
///
/// <c>FFSNet.NetworkPlayer</c> is <c>EntityBehaviour&lt;IPlayerState&gt;</c> — Bolt-derived — so
/// the build has (and needs) no bolt.dll. We therefore NEVER reference that type: every
/// <c>PlayerRegistry</c> / <c>NetworkPlayer</c> / <c>NetworkControllable</c> member is reached via
/// <see cref="AccessTools"/> and handled as <see cref="object"/>. Only once we have the
/// <c>ControllableObject</c> do we cast to the safe, non-Bolt game types
/// (<c>CharacterManager</c> → <see cref="CActor"/> → <see cref="CPlayerActor"/>).
///
/// All reflection handles are resolved ONCE and cached. If anything is missing (netcode absent,
/// a game update renamed a member) the bridge degrades to a fully disabled state: every getter
/// returns null and <see cref="LocalStableIndex"/> returns (0, total 1). Read-only; never mutates
/// game state.
///
/// It also owns the STEAM-AVATAR FETCH PUMP (<see cref="TickAvatarFetch"/>): the game's own
/// join-time fetch is broken and leaves every peer wearing the game's grey placeholder, so the
/// mod re-asks the game — through the game's own public entry points, with the game's own 64-bit
/// SteamId — until a real picture lands. The pump lives HERE and not in a tag because both tags
/// (<see cref="RemoteNameTag"/> above the head, <see cref="OwnerTag"/> on the board) read the same
/// sprite, and because the fetch must run as soon as the peer exists — not only while a tag is on
/// screen. See <see cref="ClassifyAvatar"/> for the placeholder-vs-picture distinction the whole
/// fix turns on.
/// </summary>
internal static class NetPlayerActors
{
    private static bool _init;
    private static bool _disabled;

    // PlayerRegistry (static)
    private static MethodInfo? _getPlayer;         // NetworkPlayer GetPlayer(int)
    private static PropertyInfo? _myPlayer;        // NetworkPlayer MyPlayer { get; }
    private static PropertyInfo? _participants;    // List<NetworkPlayer> Participants { get; }

    // NetworkPlayer (instance)
    private static PropertyInfo? _playerId;        // int PlayerID
    private static PropertyInfo? _username;        // string Username
    private static PropertyInfo? _avatar;          // Sprite Avatar
    private static FieldInfo? _myControllables;    // ObservableCollection<NetworkControllable> MyControllables
    private static MethodInfo? _updateAvatar;      // void UpdatePlayerProfileAvatar() — OPTIONAL (see RequestAvatarFetch)
    private static PropertyInfo? _platformPlayerId; // string PlatformPlayerId — the FULL 64-bit SteamId
    private static PropertyInfo? _platformName;    // string PlatformName ("Steam", "EpicGamesStore", ...)

    // The join column between the game's VOICE CHAT and its NETWORK PLAYERS. Added at ModBuild 297
    // for spatial voice; see Voice/VoiceSpatial.cs and .planning/VOICE-SPATIAL.md.
    //
    // BOTH SIDES CARRY THE SAME STRING, and it is NOT the one you would guess. Photon Voice's
    // per-user payload is built in BoltVoiceBridge.SetUpUsername() from
    // PlatformLayer.UserData.PlatformNetworkAccountPlayerID (BoltVoiceBridge.cs:172/176/182, "0"
    // when signed out) and comes back out as ConnectedUserVoice.PlatformAccountID. The player side
    // is set in NetworkPlayer.Attached() from the connect token's PlatformNetworkAccountPlayerID
    // (NetworkPlayer.cs:146, via PlayerRegistry.CreatePlayer :166-190). On Steam that string is the
    // 32-BIT SteamId.AccountId (PlatformUserData.cs:95-96,106) -- NOT the 64-bit SteamID64 that
    // _platformPlayerId above holds. Crossing those two is the exact mistake that made every peer
    // show a grey avatar; do not repeat it here.
    private static FieldInfo? _allPlayers;         // static List<NetworkPlayer> PlayerRegistry.AllPlayers
    private static PropertyInfo? _networkAccountId; // string NetworkPlayer.PlatformNetworkAccountPlayerID

    // PlatformUserData — the game's own avatar service. OPTIONAL: only the avatar path uses it.
    private static PropertyInfo? _userDataProp;    // static PlatformUserData PlatformLayer.UserData
    private static FieldInfo? _defaultAvatarField; // [SerializeField] Sprite PlatformUserData._defaultUserAvatarSprite
    private static MethodInfo? _getAvatarForPlayer; // void GetAvatarForNetworkPlayer(NetworkPlayer, string)

    // Steamworks (static) — OPTIONAL, only gates avatar-fetch retries
    private static PropertyInfo? _steamValid;      // static bool Steamworks.SteamClient.IsValid

    // NetworkControllable (instance)
    private static PropertyInfo? _controllableObject; // IControllable ControllableObject

    private static void EnsureInit()
    {
        if (_init)
            return;
        _init = true;
        try
        {
            Type? registry = AccessTools.TypeByName("FFSNet.PlayerRegistry");
            Type? player = AccessTools.TypeByName("FFSNet.NetworkPlayer");
            Type? controllable = AccessTools.TypeByName("FFSNet.NetworkControllable");

            _getPlayer = registry == null ? null : AccessTools.Method(registry, "GetPlayer", new[] { typeof(int) });
            _myPlayer = registry?.GetProperty("MyPlayer", BindingFlags.Public | BindingFlags.Static);
            _participants = registry?.GetProperty("Participants", BindingFlags.Public | BindingFlags.Static);

            _playerId = player?.GetProperty("PlayerID");
            _username = player?.GetProperty("Username");
            _avatar = player?.GetProperty("Avatar");
            _myControllables = player == null ? null : AccessTools.Field(player, "MyControllables");
            _updateAvatar = player == null ? null : AccessTools.Method(player, "UpdatePlayerProfileAvatar");
            // The token-derived 64-bit SteamId the entity already carries (set in Attached()) —
            // the DIRECT fetch below uses it, so it needs no live Bolt AttachToken.
            _platformPlayerId = player?.GetProperty("PlatformPlayerId") ?? player?.GetProperty("PlatformPlayerID");
            _platformName = player?.GetProperty("PlatformName");

            // OPTIONAL on purpose: their absence must disable spatial voice's mapping and nothing
            // else, so they are deliberately NOT part of the _disabled expression below.
            _allPlayers = registry == null ? null : AccessTools.Field(registry, "AllPlayers");
            _networkAccountId = player?.GetProperty("PlatformNetworkAccountPlayerID");

            _controllableObject = controllable?.GetProperty("ControllableObject");

            // PlatformUserData: the game's Steam-avatar service. We need (a) its DEFAULT
            // placeholder sprite, to tell "the game has a picture" from "the game gave up and
            // stamped its grey silhouette" (see ClassifyAvatar — this distinction IS the bug),
            // and (b) GetAvatarForNetworkPlayer as the direct fetch seam.
            Type? platformLayer = AccessTools.TypeByName("PlatformLayer");
            Type? userData = AccessTools.TypeByName("PlatformUserData");
            _userDataProp = platformLayer?.GetProperty("UserData", BindingFlags.Public | BindingFlags.Static);
            _defaultAvatarField = userData == null ? null : AccessTools.Field(userData, "_defaultUserAvatarSprite");
            _getAvatarForPlayer = userData == null ? null : AccessTools.Method(userData, "GetAvatarForNetworkPlayer");

            // Facepunch Steamworks — used only to stop avatar-fetch retries in a non-Steam
            // session. OPTIONAL on purpose: its absence must never disable the whole bridge.
            Type? steamClient = AccessTools.TypeByName("Steamworks.SteamClient");
            _steamValid = steamClient?.GetProperty("IsValid", BindingFlags.Public | BindingFlags.Static);

            _disabled = _getPlayer == null || _myPlayer == null || _participants == null
                        || _playerId == null || _username == null || _avatar == null
                        || _myControllables == null || _controllableObject == null;

            if (_disabled)
                VRLog.Warn("Net", "NetPlayerActors: FFSNet reflection incomplete — remote board/avatar identity disabled.");
        }
        catch (Exception e)
        {
            _disabled = true;
            VRLog.Error("Net", $"NetPlayerActors reflection resolution threw: {e}");
        }
    }

    /// <summary>The <see cref="CPlayerActor"/> controlled by <paramref name="playerId"/>, or null
    /// (offline, no such player, benched / non-player controllable, netcode absent).</summary>
    public static CPlayerActor? ActorFor(int playerId)
    {
        object? np = PlayerObject(playerId);
        if (np == null)
            return null;

        try
        {
            if (_myControllables!.GetValue(np) is not IEnumerable controllables)
                return null;

            foreach (object? nc in controllables)
            {
                if (nc == null)
                    continue;
                object? co = _controllableObject!.GetValue(nc);
                if (co is CharacterManager cm && cm.CharacterActor is CPlayerActor pa)
                    return pa;
            }
        }
        catch (Exception e)
        {
            VRLog.Warn("Net", $"NetPlayerActors.ActorFor({playerId}) failed: {e.Message}");
        }
        return null;
    }

    /// <summary>The Steam avatar sprite for <paramref name="playerId"/>, or null.</summary>
    public static Sprite? AvatarFor(int playerId)
    {
        object? np = PlayerObject(playerId);
        if (np == null)
            return null;
        try { return _avatar!.GetValue(np) as Sprite; }
        catch { return null; }
    }

    /// <summary>What <c>NetworkPlayer.Avatar</c> currently holds — the distinction the second
    /// MP-test defect turned on (see <see cref="ClassifyAvatar"/>).</summary>
    public enum AvatarKind
    {
        /// <summary>No sprite at all (peer not registered yet, or the fetch has not returned).</summary>
        None,
        /// <summary>The GAME'S OWN grey placeholder silhouette — i.e. the game tried and FAILED.
        /// Non-null, so a plain null check reads it as success: that was the bug.</summary>
        GameDefault,
        /// <summary>A real Steam avatar picture.</summary>
        Real,
    }

    /// <summary>Outcome of <see cref="RequestAvatarFetch"/> — drives the pump's retry policy and
    /// is written verbatim into the diagnostic line of every attempt.</summary>
    public enum AvatarFetch
    {
        /// <summary>Triggered through <c>NetworkPlayer.UpdatePlayerProfileAvatar()</c>; the sprite
        /// lands asynchronously (or not at all).</summary>
        Requested,
        /// <summary>Triggered through <c>PlatformUserData.GetAvatarForNetworkPlayer(player,
        /// player.PlatformPlayerId)</c> — the fallback that needs no live Bolt AttachToken.</summary>
        RequestedDirect,
        /// <summary>The peer has no <c>NetworkPlayer</c> in <c>PlayerRegistry.AllPlayers</c> yet
        /// (the game adds a remote player only after an ASYNC profanity-mask callback). Purely a
        /// timing miss — the pump does not count it as an attempt.</summary>
        NotRegistered,
        /// <summary>The local Steam client is not running — the game's fetch path is a hard no-op,
        /// so retrying is pointless for the whole session.</summary>
        NoSteam,
        /// <summary>Structural failure: both seams are missing or both threw. Retrying may still
        /// succeed (a seam can appear late); the pump caps attempts.</summary>
        Unavailable,
    }

    /// <summary>
    /// ACTIVELY re-trigger the game's own Steam-avatar fetch for <paramref name="playerId"/>:
    /// first <c>NetworkPlayer.UpdatePlayerProfileAvatar()</c> — the exact public seam the game's
    /// own multiplayer UI rows call (<c>UIMultiplayerUser.Show</c>,
    /// <c>PlayerPortraitVoiceComponent.Init</c>) — and, if that is missing or throws, the same
    /// call it makes one level down: <c>PlatformUserData.GetAvatarForNetworkPlayer(player,
    /// player.PlatformPlayerId)</c>.
    ///
    /// WHY THIS EXISTS (MP-test defect): the automatic fetch in <c>NetworkPlayer.Attached()</c>
    /// passes <c>PlatformNetworkAccountPlayerID</c> — on Steam the 32-BIT AccountId
    /// (<c>SteamId.AccountId</c>, see <c>PlatformUserData.PlatformAccountID</c>) — into
    /// <c>SteamFriends.GetLargeAvatarAsync(ulong)</c>, which needs the FULL 64-bit SteamId. That
    /// malformed id resolves to nobody, so the callback falls into
    /// <c>SetDefaultNetworkPlayerAvatar</c> and every peer ends up wearing the game's grey
    /// placeholder on every machine. <c>UpdatePlayerProfileAvatar()</c> instead re-reads the
    /// entity's replicated <c>PlayerToken</c> and uses its <c>PlatformPlayerID</c> — the full
    /// 64-bit SteamId — which is why the game's flat UI shows real pictures the moment one of
    /// those rows appears (on the host: when a character is assigned; on a VR client: never,
    /// because the VR flow never opens that row). It works in BOTH directions: the token rides
    /// the Bolt entity, so a client holds the HOST's token too.
    ///
    /// WHY THE SECOND SEAM: <c>UpdatePlayerProfileAvatar()</c> dereferences
    /// <c>entity.AttachToken</c>, which is a live Bolt object we cannot guarantee on a proxy.
    /// <c>NetworkPlayer.PlatformPlayerId</c> is the very same string, copied onto the entity in
    /// <c>Attached()</c> and therefore always there — so the fallback is strictly more robust and
    /// reaches the identical <c>GetAvatarForNetworkPlayer</c> the first seam ends in. Non-Steam
    /// peers get the game's default sprite through it (its <c>PlatformName != "Steam"</c> branch).
    ///
    /// Read-only stance kept: this asks the game to fill ITS OWN registry through ITS OWN public
    /// entry points with values from its own token — we mutate nothing ourselves. Never throws.
    /// </summary>
    public static AvatarFetch RequestAvatarFetch(int playerId)
    {
        EnsureInit();
        if (_disabled)
            return AvatarFetch.Unavailable;
        if (!SteamClientValid())
            return AvatarFetch.NoSteam;
        object? np = PlayerObject(playerId);
        if (np == null)
            return AvatarFetch.NotRegistered;

        if (_updateAvatar != null)
        {
            try
            {
                _updateAvatar.Invoke(np, null);
                return AvatarFetch.Requested;
            }
            catch (Exception e)
            {
                // Expected shape: the entity has no live AttachToken on this machine. Fall
                // through to the direct seam rather than giving up on the picture.
                VRLog.Info("Net", $"Steam avatar: NetworkPlayer.UpdatePlayerProfileAvatar() threw for "
                    + $"player {playerId} ({Inner(e)}) — falling back to the direct "
                    + "PlatformUserData.GetAvatarForNetworkPlayer seam.");
            }
        }

        object? userData = UserDataInstance();
        if (userData == null || _getAvatarForPlayer == null || _platformPlayerId == null)
            return AvatarFetch.Unavailable;
        try
        {
            string? id = _platformPlayerId.GetValue(np) as string;
            if (string.IsNullOrEmpty(id))
                return AvatarFetch.Unavailable;
            _getAvatarForPlayer.Invoke(userData, new object[] { np, id! });
            return AvatarFetch.RequestedDirect;
        }
        catch (Exception e)
        {
            VRLog.Info("Net", $"Steam avatar: the direct GetAvatarForNetworkPlayer seam threw for "
                + $"player {playerId} ({Inner(e)}) — no fetch path left this attempt.");
            return AvatarFetch.Unavailable;
        }
    }

    /// <summary>Unwrap the <see cref="TargetInvocationException"/> every reflection invoke wraps
    /// around the real failure, so a log line names the actual cause instead of "Exception has
    /// been thrown by the target of an invocation".</summary>
    private static string Inner(Exception e) => (e.InnerException ?? e).Message;

    /// <summary>The live <c>PlatformUserData</c> MonoBehaviour (<c>PlatformLayer.UserData</c>), or
    /// null before the platform layer boots.</summary>
    private static object? UserDataInstance()
    {
        try { return _userDataProp?.GetValue(null); }
        catch { return null; }
    }

    // The game's placeholder silhouette, resolved LAZILY: PlatformLayer.UserData is a scene
    // MonoBehaviour that does not exist when our reflection handles resolve. Cached on the first
    // successful read (it is a serialized asset reference — it never changes afterwards).
    private static Sprite? _defaultAvatar;
    private static bool _defaultAvatarResolved;

    /// <summary>
    /// The sprite <c>PlatformUserData.SetDefaultNetworkPlayerAvatar</c> stamps onto a player whose
    /// Steam fetch produced nothing — the grey silhouette. Null while the platform layer has not
    /// booted (or if the field was renamed), which <see cref="ClassifyAvatar"/> handles.
    /// </summary>
    public static Sprite? DefaultAvatarSprite()
    {
        EnsureInit();
        if (_defaultAvatarResolved)
            return _defaultAvatar;
        object? userData = UserDataInstance();
        if (userData == null || _defaultAvatarField == null)
            return null; // platform layer not up yet — retry on a later tick
        try
        {
            _defaultAvatar = _defaultAvatarField.GetValue(userData) as Sprite;
            _defaultAvatarResolved = true;
        }
        catch
        {
            _defaultAvatarResolved = true; // do not retry a throwing field read every frame
        }
        return _defaultAvatar;
    }

    /// <summary>
    /// Tell a REAL Steam picture from the game's placeholder. THIS IS THE HEART OF THE SECOND
    /// MP-test defect: the broken join-time fetch does not leave <c>Avatar</c> null, it leaves it
    /// holding <c>_defaultUserAvatarSprite</c>. A null check therefore reports "we have an avatar",
    /// no re-fetch is ever issued, and the tag shows a grey silhouette forever (host: until the
    /// game's own roster row fires the correct fetch on character assignment; VR client: never).
    ///
    /// Primary test: reference equality with <see cref="DefaultAvatarSprite"/> — exact, because
    /// the game assigns that one shared asset instance. FALLBACK when the placeholder cannot be
    /// resolved (field renamed / platform layer not up): a real avatar's texture is created at
    /// runtime by <c>CreateTexture2DFromSteamImage</c> (<c>new Texture2D(w, h)</c>) and therefore
    /// carries an EMPTY name, while any imported placeholder asset carries its import name. A
    /// wrong verdict costs at most a few extra (capped, harmless) re-fetches.
    /// </summary>
    public static AvatarKind ClassifyAvatar(Sprite? sprite)
    {
        if (sprite == null)
            return AvatarKind.None;
        Sprite? fallbackSprite = DefaultAvatarSprite();
        if (fallbackSprite != null)
            return ReferenceEquals(sprite, fallbackSprite) ? AvatarKind.GameDefault : AvatarKind.Real;
        Texture? tex = sprite.texture;
        return tex != null && string.IsNullOrEmpty(tex.name) ? AvatarKind.Real : AvatarKind.GameDefault;
    }

    // ---- avatar fetch pump ----------------------------------------------------------------

    /// <summary>Seconds between two fetch attempts for the same peer. The Steam round trip is
    /// ~1 s, so this is "ask again once the previous ask has had its chance".</summary>
    private const float FetchRetrySeconds = 3f;

    /// <summary>Hard cap on fetch attempts per peer (≈36 s with <see cref="FetchRetrySeconds"/>).
    /// After it we stop ASKING but keep WATCHING: a sprite the game fills later is still picked
    /// up and logged, and the tags still swap it in.</summary>
    private const int FetchMaxAttempts = 12;

    /// <summary>How often the pump looks at a peer's sprite (seconds). Decoupled from the retry
    /// interval so "the picture arrived" is logged promptly while the ASKING stays sparse.</summary>
    private const float PollSeconds = 0.5f;

    /// <summary>Per-peer state of the avatar fetch pump. Reset when the peer's
    /// <c>NetworkPlayer</c> INSTANCE changes (rejoin / new session), never keyed on anything the
    /// game could reuse.</summary>
    private sealed class AvatarPump
    {
        public object? Player;      // identity of the NetworkPlayer we are pumping
        public int Attempts;
        public float NextPollAt;
        public float NextAttemptAt;
        public bool Stopped;        // no more ASKING (cap reached / no Steam / non-Steam peer)
        public bool Applied;        // a real picture landed — logged once, pump idle
        public bool LoggedWaiting;  // "peer not in the registry yet" — logged once
        public string Seam = "the game itself (no mod fetch was needed)";
        public float StartedAt;
    }

    private static readonly Dictionary<int, AvatarPump> _avatarPumps = new();

    /// <summary>
    /// Drive the Steam-avatar fetch for one remote peer. Called once per frame per remote avatar
    /// by <see cref="NetAvatarDriver"/> — deliberately NOT from a tag: the fetch must run as soon
    /// as the PEER exists, independent of whether a name tag is currently visible, of the
    /// <c>[Net] NameTags</c> config, and of the local role (host or client).
    ///
    /// Policy: while the game holds no real picture (<see cref="AvatarKind.None"/> or
    /// <see cref="AvatarKind.GameDefault"/>), ask <see cref="RequestAvatarFetch"/> every
    /// <see cref="FetchRetrySeconds"/>, at most <see cref="FetchMaxAttempts"/> times; stop for
    /// good on <see cref="AvatarFetch.NoSteam"/>. A peer that is not in the registry yet does not
    /// burn an attempt. Every attempt and every outcome is logged at INFO (bounded by the cap),
    /// and exactly one line reports the landed picture and which seam produced it. Never throws:
    /// the reflection layer swallows, and the caller ticks this inside its per-avatar catch.
    /// </summary>
    public static void TickAvatarFetch(int playerId)
    {
        EnsureInit();
        if (_disabled)
            return;

        if (!_avatarPumps.TryGetValue(playerId, out AvatarPump pump))
        {
            pump = new AvatarPump { StartedAt = Time.unscaledTime };
            _avatarPumps[playerId] = pump;
        }

        float now = Time.unscaledTime;
        if (now < pump.NextPollAt)
            return;
        pump.NextPollAt = now + PollSeconds;

        object? np = PlayerObject(playerId);
        if (np == null)
        {
            // The game adds a REMOTE player to PlayerRegistry.AllPlayers only from an async
            // callback (client: the profanity mask; host: the PlayerEntityInitializedEvent), so
            // this is normal for the first seconds after a peer's first packet.
            if (!pump.LoggedWaiting)
            {
                pump.LoggedWaiting = true;
                VRLog.Info("Net", $"Steam avatar for player {playerId}: the game has no NetworkPlayer "
                    + "in PlayerRegistry.AllPlayers yet (a remote player is added from an async "
                    + "callback after join) — polling; no attempt spent.");
            }
            return;
        }
        if (!ReferenceEquals(np, pump.Player))
        {
            // First sight, or the peer rejoined with a fresh entity: start the policy over.
            pump.Player = np;
            pump.Attempts = 0;
            pump.NextAttemptAt = 0f;
            pump.Stopped = false;
            pump.Applied = false;
            pump.StartedAt = now;
            pump.Seam = "the game itself (no mod fetch was needed)";
        }

        Sprite? current = AvatarFor(playerId);
        AvatarKind kind = ClassifyAvatar(current);
        if (kind != AvatarKind.Real && !pump.Stopped && !IsSteamPeer(np, out string platform))
        {
            // The game only ever hands a NON-Steam peer its default sprite
            // (PlatformUserData.GetAvatarForNetworkPlayer, PlatformName != "Steam" branch), so the
            // placeholder is the correct FINAL state here — asking twelve times cannot change it.
            pump.Stopped = true;
            VRLog.Info("Net", $"Steam avatar for player {playerId}: the peer is on platform "
                + $"'{platform}', not Steam — the game has no Steam picture for them and its "
                + "default placeholder is the final answer. No fetch attempts.");
        }
        if (kind == AvatarKind.Real)
        {
            if (!pump.Applied)
            {
                pump.Applied = true;
                Texture? tex = current!.texture;
                VRLog.Info("Net", $"Steam avatar APPLIED for player {playerId} "
                    + $"({NameFor(playerId) ?? "?"}): real picture {(tex != null ? $"{tex.width}x{tex.height}" : "?")} "
                    + $"from {pump.Seam}, {pump.Attempts} fetch attempt(s) in "
                    + $"{now - pump.StartedAt:0.0}s — the name tag and the board owner tag show it now.");
            }
            return;
        }
        pump.Applied = false; // sprite went away (session teardown) — allow a fresh APPLIED line
        if (pump.Stopped || now < pump.NextAttemptAt)
            return;
        pump.NextAttemptAt = now + FetchRetrySeconds;

        if (pump.Attempts >= FetchMaxAttempts)
        {
            pump.Stopped = true;
            VRLog.Info("Net", $"Steam avatar for player {playerId}: after {FetchMaxAttempts} fetch "
                + $"attempts the game still only has {Describe(kind)} — no further asking. The tag "
                + "keeps what the game has and still swaps in a picture if one lands later.");
            return;
        }

        string before = Describe(kind);
        pump.Attempts++;
        AvatarFetch result = RequestAvatarFetch(playerId);
        switch (result)
        {
            case AvatarFetch.Requested:
                pump.Seam = "NetworkPlayer.UpdatePlayerProfileAvatar() (64-bit PlatformPlayerID)";
                VRLog.Info("Net", $"Steam avatar fetch {pump.Attempts}/{FetchMaxAttempts} for player "
                    + $"{playerId}: sprite before = {before}; asked the game via "
                    + "NetworkPlayer.UpdatePlayerProfileAvatar() (the seam its own MP rows use — it "
                    + "feeds Steam the FULL 64-bit PlatformPlayerID from the replicated PlayerToken, "
                    + "unlike the broken 32-bit AccountId the join-time fetch uses); result = "
                    + "REQUESTED, the picture lands asynchronously.");
                break;
            case AvatarFetch.RequestedDirect:
                pump.Seam = "PlatformUserData.GetAvatarForNetworkPlayer(64-bit PlatformPlayerId)";
                VRLog.Info("Net", $"Steam avatar fetch {pump.Attempts}/{FetchMaxAttempts} for player "
                    + $"{playerId}: sprite before = {before}; asked the game via "
                    + "PlatformUserData.GetAvatarForNetworkPlayer(player, player.PlatformPlayerId) "
                    + "— the direct 64-bit seam, used because UpdatePlayerProfileAvatar was missing "
                    + "or threw; result = REQUESTED, the picture lands asynchronously.");
                break;
            case AvatarFetch.NotRegistered:
                pump.Attempts--; // pure timing miss — do not spend the budget on it
                VRLog.Info("Net", $"Steam avatar fetch for player {playerId}: the peer left "
                    + "PlayerRegistry.AllPlayers between the poll and the ask — retrying in "
                    + $"{FetchRetrySeconds:0}s, attempt not counted.");
                break;
            case AvatarFetch.NoSteam:
                pump.Stopped = true;
                VRLog.Info("Net", $"Steam avatar fetch {pump.Attempts}/{FetchMaxAttempts} for player "
                    + $"{playerId}: STEAM UNAVAILABLE (Steamworks.SteamClient.IsValid == false — "
                    + "non-Steam session) — a Steam picture is impossible here; the tag stays with "
                    + $"{before} and no further attempts are made.");
                break;
            default:
                VRLog.Info("Net", $"Steam avatar fetch {pump.Attempts}/{FetchMaxAttempts} for player "
                    + $"{playerId}: sprite before = {before}; result = SEAM UNAVAILABLE (neither "
                    + "NetworkPlayer.UpdatePlayerProfileAvatar nor "
                    + "PlatformUserData.GetAvatarForNetworkPlayer could be invoked) — retrying in "
                    + $"{FetchRetrySeconds:0}s.");
                break;
        }
    }

    /// <summary>Whether the peer joined from Steam (<c>NetworkPlayer.PlatformName</c>). UNKNOWN
    /// counts as Steam: the property could only be unreadable if the game changed, and erring
    /// towards "try the fetch" costs a few capped attempts while erring the other way would kill
    /// the picture outright.</summary>
    private static bool IsSteamPeer(object networkPlayer, out string platform)
    {
        platform = "?";
        try
        {
            if (_platformName?.GetValue(networkPlayer) is not string name || name.Length == 0)
                return true;
            platform = name;
            return name == "Steam";
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Human-readable sprite state for the diagnostics — the log has to make the
    /// "non-null but only the placeholder" case unmistakable, because that is exactly the state a
    /// null check misread.</summary>
    private static string Describe(AvatarKind kind) => kind switch
    {
        AvatarKind.None => "NO sprite at all",
        AvatarKind.GameDefault => "the game's DEFAULT grey placeholder (its own fetch failed)",
        _ => "a real Steam picture",
    };

    /// <summary>Drop a peer's pump state (avatar torn down / player left), so a rejoin starts
    /// with a fresh attempt budget instead of an exhausted one.</summary>
    public static void ForgetAvatarFetch(int playerId) => _avatarPumps.Remove(playerId);

    /// <summary>True when the local Facepunch Steam client reads as running. Errs on TRUE when the
    /// property cannot be resolved/read — the game's fetch path no-ops safely by itself, and the
    /// caller's attempt cap bounds the waste.</summary>
    private static bool SteamClientValid()
    {
        try
        {
            return _steamValid == null || (_steamValid.GetValue(null) is bool b && b);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>The (bad-word-masked) username for <paramref name="playerId"/>, or null.</summary>
    public static string? NameFor(int playerId)
    {
        object? np = PlayerObject(playerId);
        if (np == null)
            return null;
        try { return _username!.GetValue(np) as string; }
        catch { return null; }
    }

    /// <summary>
    /// The PlayerID of an already-obtained <c>NetworkPlayer</c> handled as <see cref="object"/>
    /// (the Bolt-derived type is never referenced — class doc). 0 when null/disabled/unreadable.
    /// Used by <see cref="PlayerBadges"/>, which walks game UI rows whose player fields it can
    /// only read reflectively for the same reason.
    /// </summary>
    public static int PlayerIdOf(object? networkPlayer)
    {
        EnsureInit();
        if (_disabled || networkPlayer == null)
            return 0;
        try { return _playerId!.GetValue(networkPlayer) is int id ? id : 0; }
        catch { return 0; }
    }

    /// <summary>
    /// The PlayerID of the player whose platform NETWORK ACCOUNT id is
    /// <paramref name="networkAccountId"/>, or 0 when there is no such player (not joined yet, not
    /// in this session, signed out, or the reflection is unavailable).
    ///
    /// <para><b>THIS IS THE VOICE-CHAT TO AVATAR MAPPING, AND IT IS THE GAME'S OWN.</b> It is not a
    /// heuristic and it is not "the only other player in the room": it is a string equality the
    /// shipped game already performs, in two places, to decide which portrait and which name belong
    /// to a voice row -- <c>Script.GUI.IngameMenu.EscMenuVoiceChat/PlayerPortraitVoiceComponent.cs:41-45</c>
    /// and <c>PlayerNameVoiceComponent.cs:146-150</c>, both of which are literally
    /// <c>PlayerRegistry.AllPlayers.FirstOrDefault(x =&gt; x.PlatformNetworkAccountPlayerID ==
    /// accountId)</c>. The game also ships the same lookup as a helper at
    /// <c>FFSNet/PlayerRegistry.cs:266</c>. We reproduce the FirstOrDefault form rather than call
    /// the helper only because the helper's signature names the Bolt-derived <c>NetworkPlayer</c>
    /// type, which this class never references (class doc).</para>
    ///
    /// <para><b>WHAT A MISMATCH LOOKS LIKE, because it must be visible in a log and not guessed
    /// at.</b> A miss returns 0 and the caller keeps that peer's voice NON-spatial -- i.e. exactly
    /// vanilla behaviour, never silence. <c>Voice/VoiceSpatial.cs</c> logs one line naming the
    /// account id, the display name and every candidate it compared against, once per unmatched
    /// voice user, so a failed mapping reads as "VOICE SPATIAL: no network player for account ..."
    /// with the roster printed beside it. An id that matched the WRONG player would instead show up
    /// as a voice arriving from the wrong mask, and the same line names which player id it bound
    /// to.</para>
    ///
    /// <para>An empty or "0" account id (the signed-out fallback, BoltVoiceBridge.cs:182) is
    /// rejected outright: several signed-out peers would otherwise all match each other.</para>
    /// </summary>
    public static int PlayerIdForNetworkAccount(string? networkAccountId)
    {
        EnsureInit();
        if (_disabled || _allPlayers == null || _networkAccountId == null)
            return 0;
        if (string.IsNullOrEmpty(networkAccountId) || networkAccountId == "0")
            return 0;

        try
        {
            if (_allPlayers.GetValue(null) is not System.Collections.IEnumerable all)
                return 0;
            foreach (object? np in all)
            {
                if (np == null)
                    continue;
                if (_networkAccountId.GetValue(np) as string == networkAccountId)
                    return _playerId!.GetValue(np) is int id ? id : 0;
            }
        }
        catch { /* an unreadable registry is a miss, never a throw */ }
        return 0;
    }

    /// <summary>
    /// Every current player as (PlayerID, network account id, username), appended to
    /// <paramref name="into"/>. Diagnostic only: it exists so that a failed
    /// <see cref="PlayerIdForNetworkAccount"/> can print WHAT IT COMPARED AGAINST instead of only
    /// reporting that it found nothing. A census that says "no match" without naming the candidates
    /// is the kind of instrument this project has been burned by before.
    /// </summary>
    public static void CollectRoster(List<(int Id, string? Account, string? Name)> into)
    {
        EnsureInit();
        if (_disabled || _allPlayers == null || _networkAccountId == null || into == null)
            return;
        try
        {
            if (_allPlayers.GetValue(null) is not System.Collections.IEnumerable all)
                return;
            foreach (object? np in all)
            {
                if (np == null)
                    continue;
                into.Add((_playerId!.GetValue(np) is int id ? id : 0,
                          _networkAccountId.GetValue(np) as string,
                          _username!.GetValue(np) as string));
            }
        }
        catch { /* diagnostic only */ }
    }

    /// <summary>
    /// The LOCAL player's PlayerID, or 0 when offline / absent. The same value as
    /// <c>INetTransport.LocalPlayerId</c>, reachable statically -- that one lives behind a private
    /// field of <see cref="NetAvatarDriver"/> and has no accessor.
    /// </summary>
    public static int LocalPlayerId()
    {
        EnsureInit();
        if (_disabled)
            return 0;
        try
        {
            object? mp = _myPlayer!.GetValue(null);
            return mp == null ? 0 : _playerId!.GetValue(mp) is int id ? id : 0;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Index of the LOCAL player among the current participants, sorted ascending by PlayerID,
    /// with <paramref name="total"/> = participant count. Deterministic across clients so the
    /// spawn circle gives each player a distinct azimuth. Returns (0, total 1) when offline /
    /// absent / netcode missing.
    /// </summary>
    public static int LocalStableIndex(out int total)
    {
        total = 1;
        EnsureInit();
        if (_disabled)
            return 0;

        try
        {
            object? me = _myPlayer!.GetValue(null);
            if (me == null || _playerId!.GetValue(me) is not int myId)
                return 0;

            if (_participants!.GetValue(null) is not IEnumerable participants)
                return 0;

            var ids = new List<int>();
            foreach (object? p in participants)
            {
                if (p != null && _playerId!.GetValue(p) is int pid)
                    ids.Add(pid);
            }
            if (ids.Count == 0)
                return 0;

            ids.Sort();
            total = ids.Count;
            int index = ids.IndexOf(myId);
            return index < 0 ? 0 : index;
        }
        catch (Exception e)
        {
            VRLog.Warn("Net", $"NetPlayerActors.LocalStableIndex failed: {e.Message}");
            total = 1;
            return 0;
        }
    }

    /// <summary>Reflection-fetch the <c>NetworkPlayer</c> object for an id (as <see cref="object"/>
    /// — never the Bolt type). Null when disabled / not found.</summary>
    private static object? PlayerObject(int playerId)
    {
        EnsureInit();
        if (_disabled)
            return null;
        try { return _getPlayer!.Invoke(null, new object[] { playerId }); }
        catch { return null; }
    }
}
