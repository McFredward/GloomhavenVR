using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Board.Patches;

/// <summary>
/// MP BUG #7 (name label): the flat game shows the pinging PLAYER'S NAME next to every hex
/// ping — a <c>UIPingTooltip</c> on a SCREEN-SPACE canvas positioned by projecting the hex
/// through <c>WorldspaceUITools.WorldspaceCamera</c> (UIPingTooltip.cs:113-121). In VR that
/// projection uses the flat scenario camera, so the label is invisible/misplaced for a VR
/// player; only the world-space 3D highlight survives.
///
/// FIX: postfix on the game's single ping-display chokepoint,
/// <c>PingManager.Ping3DElement(GameObject element, NetworkPlayer player)</c> (private; BOTH
/// public entries funnel into it, and the network receive path
/// <c>ProxyPingHex → Ping3DElementMultiPlayer</c> does too — PingManager.cs:63-88,135). So the
/// world-space VR name tag appears for our own pings AND for every ping received from any
/// peer, flat or VR, with the exact name the flat game would show
/// (<c>player?.Username ?? PlatformLayer.UserData.UserName</c>, PingManager.cs:81).
///
/// The <c>player</c> argument is declared as <c>object</c>: <c>FFSNet.NetworkPlayer</c> is
/// Bolt-derived and this mod deliberately has no bolt.dll reference (see
/// <see cref="Net.NetPlayerActors"/>); Username/PlayerID are read via cached reflection.
///
/// Vanilla-mirroring rules (PingManager.cs:73-93): one live ping per (element, player) — a
/// repeat while shown is a no-op; a NEW ping removes the same player's previous ping and any
/// other ping on the same element (<c>HidePing(player, element)</c>). Lifetime mirrors the
/// manager's own <c>lifetimePing</c> (2 s default), with a short fade tail.
///
/// COSMETIC ONLY: renders a mod-owned world-space TMP label; never touches game state, no
/// wire bytes of ours (the name arrives via the game's own PingHex payload/registry —
/// second-source-of-truth rule, INVARIANTS-Net-Rig.md). Only active while VR runs; the flat
/// tooltip keeps doing its job on desktop. Degrades to a silent no-op if reflection fails.
/// </summary>
[HarmonyPatch]
internal static class PingNameTag_Patch
{
    /// <summary>Private chokepoint — resolved by name (single method, no overloads), so this
    /// class never types the Bolt-derived NetworkPlayer parameter. Degrades by design:
    /// TargetMethod may return null on a changed game build (patch then no-ops).</summary>
    private static MethodBase? TargetMethod()
    {
        MethodBase? m = AccessTools.Method(typeof(PingManager), "Ping3DElement");
        if (m == null)
            VRLog.Warn("Board", "[Ping] PingManager.Ping3DElement not found — ping name tags disabled " +
                "(game's own ping visuals unaffected).");
        return m;
    }

    private static void Postfix(GameObject element, object? player, PingManager __instance)
    {
        try
        {
            if (!VRSession.IsRunning || element == null)
                return;
            PingNameTag.OnPingShown(element, player, __instance);
        }
        catch (Exception e)
        {
            VRLog.Warn("Board", $"[Ping] name-tag postfix failed (suppressed): {e.Message}");
        }
    }
}

/// <summary>
/// The world-space name label itself: an unlit-style TMP line floating above the pinged hex,
/// billboarded to the local head every frame (same convention as <see cref="Net.OwnerTag"/>:
/// TMP reads from −Z, so aim +Z away from the head). Self-expires after the manager's ping
/// lifetime with an alpha fade. Sizes are in WORLD units relative to the game's hex tile
/// (~1.72 world units across), so the tag reads like a small caption over the tile at any
/// rig scale.
/// </summary>
internal sealed class PingNameTag : MonoBehaviour
{
    // World-unit metrics (game tile ≈ 1.72 world units across).
    private const float Rise = 2.4f;        // label height above the hex center
    private const float Width = 4.6f;       // text box width (~2.7 tiles, fits long names)
    private const float Height = 0.85f;     // text box height (TmpFit caps line fill)
    private const float FadeTail = 0.35f;   // alpha fade-out at end of life (seconds)
    private const float DefaultLifetime = 2f; // PingManager.lifetimePing fallback

    /// <summary>Live tags, for the vanilla one-per-(element,player) / replacement rules.</summary>
    private static readonly List<PingNameTag> Live = new();

    // Cached reflection into FFSNet.NetworkPlayer (Bolt-derived — never typed here) and the
    // local-name fallback chain PlatformLayer.UserData.UserName (platform assembly).
    private static bool _reflected;
    private static PropertyInfo? _playerId;   // int NetworkPlayer.PlayerID
    private static PropertyInfo? _username;   // string NetworkPlayer.Username (already bad-word-masked)
    private static PropertyInfo? _userData;   // static PlatformLayer.UserData
    private static PropertyInfo? _userName;   // PlatformUserData.UserName

    private int _elementId;
    private int _playerKey;
    private float _dieAt;                    // unscaled time
    private TextMeshPro? _label;
    private Color _baseColor;
    private Action? _tickCached;             // [Optimize] CacheTickDelegates — see BoardPing.Update

    /// <summary>Show (or vanilla-dedupe) the tag for one <c>Ping3DElement</c> call.</summary>
    internal static void OnPingShown(GameObject element, object? player, PingManager manager)
    {
        EnsureReflected();

        int playerKey = 0;
        string? name = null;
        if (player != null)
        {
            try
            {
                playerKey = _playerId?.GetValue(player) is int id ? id : player.GetHashCode();
                name = _username?.GetValue(player) as string;
            }
            catch { /* identity unreadable → local fallback below */ }
        }
        if (string.IsNullOrEmpty(name))
            name = LocalUserName();
        if (string.IsNullOrEmpty(name))
            name = playerKey != 0 ? $"Player {playerKey}" : null; // same fallback OwnerTag ships
        if (name == null)
            return; // nothing presentable — cosmetic feature, skip silently

        int elementId = element.GetInstanceID();

        // Vanilla IsPingShown mirror: same element + same player while alive → no-op.
        for (int i = 0; i < Live.Count; i++)
        {
            PingNameTag t = Live[i];
            if (t != null && t._elementId == elementId && t._playerKey == playerKey)
                return;
        }

        // Vanilla HidePing(player, element) mirror: a new ping replaces the same player's
        // previous tag and any tag already sitting on this element.
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            PingNameTag t = Live[i];
            if (t == null)
            {
                Live.RemoveAt(i);
                continue;
            }
            if (t._playerKey == playerKey || t._elementId == elementId)
            {
                // Eager removal: Unity's Destroy is end-of-frame, so a same-frame follow-up
                // ping must not dedupe against this dying tag (vanilla removes instantly too).
                Live.RemoveAt(i);
                UnityEngine.Object.Destroy(t.gameObject);
            }
        }

        float lifetime = DefaultLifetime;
        try
        {
            // Publicized private serialized field — the game's own ping lifetime.
            lifetime = manager.lifetimePing > 0f ? manager.lifetimePing : DefaultLifetime;
        }
        catch { /* keep default */ }

        var go = new GameObject($"GloomhavenVR.PingNameTag[{playerKey}]");
        go.transform.position = element.transform.position + Vector3.up * Rise;
        PingNameTag tag = go.AddComponent<PingNameTag>();
        tag._elementId = elementId;
        tag._playerKey = playerKey;
        tag._dieAt = Time.unscaledTime + lifetime;
        tag.Build(name!);
        Live.Add(tag);
    }

    private void Build(string name)
    {
        _label = gameObject.AddComponent<TextMeshPro>();
        _label.text = name;
        _label.alignment = TextAlignmentOptions.Center;
        _baseColor = new Color(1f, 0.95f, 0.85f);   // OwnerTag's warm off-white
        _label.color = _baseColor;
        _label.fontStyle = FontStyles.Bold;
        TmpFit.Fit(_label, Width, Height, wrap: false);
        WorldUI.MrBacking.Label(_label); // free-floating over the room in MR (plate dies with the tag)
        VRLayers.Apply(gameObject);
    }

    private void Update()
    {
        TickGuard.Run("Board.PingTag", PerfConfig.CacheDelegates ? _tickCached ??= Tick : Tick);
    }

    private void Tick()
    {
        float now = Time.unscaledTime;
        if (now >= _dieAt)
        {
            Destroy(gameObject);
            return;
        }

        // Fade tail (alpha only; vanilla hides via a GUI animator we don't have here).
        if (_label != null)
        {
            float remain = _dieAt - now;
            float a = remain < FadeTail ? remain / FadeTail : 1f;
            if (!Mathf.Approximately(_label.color.a, a))
                _label.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, a);
        }

        // Billboard toward the local head (aim +Z AWAY — TMP reads from −Z).
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
        {
            Vector3 away = transform.position - head.transform.position;
            if (away.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
    }

    private void OnDestroy()
    {
        Live.Remove(this);
    }

    /// <summary>Hot-reload hygiene (BoardModule.Shutdown): destroy live tags, clear statics.</summary>
    internal static void Reset()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            PingNameTag t = Live[i];
            if (t != null)
                UnityEngine.Object.Destroy(t.gameObject);
        }
        Live.Clear();
    }

    /// <summary>The flat game's own local-name source: <c>PlatformLayer.UserData.UserName</c>
    /// (PingManager.cs:81), via reflection — PlatformUserData lives in a platform assembly the
    /// mod does not reference.</summary>
    private static string? LocalUserName()
    {
        try
        {
            object? userData = _userData?.GetValue(null);
            return userData == null ? null : _userName?.GetValue(userData) as string;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureReflected()
    {
        if (_reflected)
            return;
        _reflected = true;
        try
        {
            Type? networkPlayer = AccessTools.TypeByName("FFSNet.NetworkPlayer");
            _playerId = networkPlayer?.GetProperty("PlayerID");
            _username = networkPlayer?.GetProperty("Username");

            Type? platformLayer = AccessTools.TypeByName("PlatformLayer");
            _userData = platformLayer?.GetProperty("UserData", BindingFlags.Public | BindingFlags.Static);
            Type? userDataType = _userData?.PropertyType;
            _userName = userDataType?.GetProperty("UserName");

            if (_username == null)
                VRLog.Warn("Board", "[Ping] NetworkPlayer.Username not resolvable — ping name tags " +
                    "fall back to 'Player <id>'.");
        }
        catch (Exception e)
        {
            VRLog.Warn("Board", $"[Ping] name-tag reflection resolution threw: {e.Message}");
        }
    }
}
