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

            _controllableObject = controllable?.GetProperty("ControllableObject");

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
