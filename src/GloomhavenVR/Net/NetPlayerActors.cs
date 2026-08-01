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

            _controllableObject = controllable?.GetProperty("ControllableObject");

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

    /// <summary>Outcome of <see cref="RequestAvatarFetch"/> — drives the caller's retry policy.</summary>
    public enum AvatarFetch
    {
        /// <summary>The game's fetch was triggered; the sprite lands asynchronously (or not at all).</summary>
        Requested,
        /// <summary>The local Steam client is not running — the game's fetch path is a hard no-op,
        /// so retrying is pointless for the whole session.</summary>
        NoSteam,
        /// <summary>Transient or structural failure (player not in the registry yet, seam missing,
        /// invoke threw). Retrying later may succeed; the caller caps attempts.</summary>
        Unavailable,
    }

    /// <summary>
    /// ACTIVELY re-trigger the game's own Steam-avatar fetch for <paramref name="playerId"/> by
    /// invoking <c>NetworkPlayer.UpdatePlayerProfileAvatar()</c> — the exact public seam the game's
    /// own multiplayer UI rows call (<c>UIMultiplayerUser.Show</c>,
    /// <c>PlayerPortraitVoiceComponent.Init</c>).
    ///
    /// WHY THIS EXISTS (MP-test defect): the automatic fetch in <c>NetworkPlayer.Attached()</c>
    /// passes <c>PlatformNetworkAccountPlayerID</c> — on Steam the 32-BIT AccountId
    /// (<c>SteamId.AccountId</c>, see <c>PlatformUserData.PlatformAccountID</c>) — into
    /// <c>SteamFriends.GetLargeAvatarAsync(ulong)</c>, which needs the FULL 64-bit SteamId. That
    /// malformed id's fetch never lands, so <c>NetworkPlayer.Avatar</c> stays null after join on
    /// every machine. <c>UpdatePlayerProfileAvatar()</c> instead re-reads the entity's replicated
    /// <c>PlayerToken</c> and uses its <c>PlatformPlayerID</c> — the full 64-bit SteamId — which is
    /// why the game's flat UI shows pictures the moment one of those rows appears (on the host:
    /// when a character is assigned). It works in BOTH directions: the token rides the Bolt entity,
    /// so a client holds the HOST's token too. Non-Steam peers get the game's default sprite via
    /// the same call (its <c>PlatformName != "Steam"</c> branch), which also ends the caller's
    /// retry loop.
    ///
    /// Read-only stance kept: this asks the game to fill ITS OWN registry through ITS OWN public
    /// UI seam with values from its own token — we mutate nothing ourselves. Never throws.
    /// </summary>
    public static AvatarFetch RequestAvatarFetch(int playerId)
    {
        EnsureInit();
        if (_disabled || _updateAvatar == null)
            return AvatarFetch.Unavailable;
        if (!SteamClientValid())
            return AvatarFetch.NoSteam;
        object? np = PlayerObject(playerId);
        if (np == null)
            return AvatarFetch.Unavailable;
        try
        {
            _updateAvatar.Invoke(np, null);
            return AvatarFetch.Requested;
        }
        catch (Exception e)
        {
            VRLog.Warn("Net", $"NetPlayerActors.RequestAvatarFetch({playerId}) failed: {e.Message}");
            return AvatarFetch.Unavailable;
        }
    }

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
