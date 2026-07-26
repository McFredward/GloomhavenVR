using System;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// Feature #3: point the LASER at a hex and press the dominant-hand "A" button to
/// fire the game's OWN ping on that hex — a 3D highlight + a floating tooltip
/// (<c>PingManager.Ping3DElementSinglePlayer</c>). In multiplayer the game/PingManager
/// path already carries a shown ping to teammates, so we deliberately use the SINGLE-
/// player entry (no <c>NetworkPlayer</c> / bolt typing in this mod).
///
/// Wiring (mirrors <c>FigureGrabDriver</c>): a MonoBehaviour added to the Board module's
/// hidden driver GameObject, so it is constructed by <see cref="BoardModule.Init"/> and
/// self-ticks in <c>Update</c>. The tick body runs under the shared <see cref="TickGuard"/>
/// so a throw here is isolated + attributed and can never starve the board tick.
///
/// Button: the DOMINANT (primary) hand's <c>primaryButton</c> = the "A" face button on a
/// right-handed Quest layout, read as the DOWN edge (<see cref="VRHand.PrimaryDown"/>) so
/// exactly one ping fires per press. Conflict check: the only other consumer of a hand
/// <c>primaryButton</c> in the mod is the pause-menu "X", which is bound to the
/// NON-dominant hand (EscMenu on the off hand); the dominant primaryButton is otherwise
/// unused (RayInteractor/board-click use the TRIGGER, AoE/turn use the thumbstick, grab
/// uses grip/trigger). So dominant "A" is free — no reassignment needed.
///
/// Hex resolution: reuses <see cref="BoardPick"/> (the single shared VR pick). We ping
/// only when <c>BoardPick.HasHit</c> AND the hit collider resolves to a
/// <c>TileBehaviour.m_ClientTile.m_GameObject</c> — the exact 3D element the game itself
/// pings and the same element <c>BoardPick</c> snaps its cursor to.
///
/// The <c>PingManager</c> call is reflection-guarded: if the method can't be resolved at
/// runtime (game update / stripped build) it degrades to a no-op after one warning rather
/// than throwing every press.
/// </summary>
internal sealed class BoardPing : MonoBehaviour
{
    /// <summary>Minimum gap between pings (seconds, unscaled) — anti-spam on top of the press edge.</summary>
    private const float Cooldown = 0.2f;

    private static bool _resolved;
    private static MethodInfo? _pingSinglePlayer;
    private static bool _warnedUnresolved;

    private float _lastPingTime = float.NegativeInfinity;

    /// <summary>Cached tick delegate — see the allocation note in <see cref="Update"/>.</summary>
    private System.Action? _tickCached;

    private void Update()
    {
        // [Optimize] CacheTickDelegates (2026-07 perf pass): passing the INSTANCE method group
        // `Tick` straight to TickGuard.Run created a brand-new Action every single frame. One
        // delegate is ~64 bytes, this mod had seven such sites, and at 90 Hz that is a steady
        // ~40 kB/s of pure ceremony feeding the gen0 collector — whose pauses are precisely the
        // kind of frame-time spike the player reports as the world "juddering" on a fast head
        // turn. Caching it is behaviour-identical work removal. The toggle exists only so the
        // hypothesis can be A/B'd on hardware against the [Perf] gc counters.
        TickGuard.Run("Board.Ping", PerfConfig.CacheDelegates ? _tickCached ??= Tick : Tick);
    }

    private void Tick()
    {
        VRHand? hand = VRHands.Primary;
        if (hand == null || !hand.HasPose)
            return;

        // Edge-triggered: one ping per press of the dominant "A" (primaryButton).
        if (!hand.PrimaryDown)
            return;

        // Only ping a real hex actually under the laser/near pick.
        if (!BoardPick.HasHit)
            return;

        GameObject? hex = ResolveHex();
        if (hex == null)
            return;

        if (Time.unscaledTime - _lastPingTime < Cooldown)
            return;

        if (!EnsureResolved())
            return;

        try
        {
            PingManager? manager = PingManager.Instance;
            if (manager == null)
                return; // no scenario ping manager live yet — silent no-op.

            _pingSinglePlayer!.Invoke(manager, new object[] { hex });
            _lastPingTime = Time.unscaledTime;
            VRLog.Info("Board", $"[Ping] pinged hex '{hex.name}'.");
        }
        catch (Exception ex)
        {
            VRLog.Warn("Board", $"[Ping] PingManager.Ping3DElementSinglePlayer threw: {ex}");
        }
    }

    /// <summary>
    /// The 3D hex GameObject under the current VR pick, or null. Mirrors
    /// <c>BoardPick.ResolveCursorWorld</c>: hit collider → parent <c>TileBehaviour</c> →
    /// <c>m_ClientTile.m_GameObject</c> (verified vs GH.Runtime: TileBehaviour.cs:14
    /// <c>public CClientTile m_ClientTile;</c>, CClientTile.cs:7 <c>public GameObject m_GameObject;</c>).
    /// </summary>
    private static GameObject? ResolveHex()
    {
        Collider? collider = BoardPick.HitCollider;
        if (collider == null)
            return null;

        TileBehaviour? tile = collider.GetComponentInParent<TileBehaviour>();
        if (tile == null || tile.m_ClientTile == null)
            return null;

        return tile.m_ClientTile.m_GameObject;
    }

    /// <summary>
    /// Resolve (once) the publicized Singleton's
    /// <c>Ping3DElementSinglePlayer(GameObject)</c>. On failure, warn once and stay a
    /// no-op. Uses reflection so a renamed/removed method degrades gracefully instead of
    /// throwing a MissingMethodException every press.
    /// </summary>
    private static bool EnsureResolved()
    {
        if (_resolved)
            return _pingSinglePlayer != null;

        _resolved = true;
        try
        {
            _pingSinglePlayer = typeof(PingManager).GetMethod(
                "Ping3DElementSinglePlayer",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(GameObject) },
                modifiers: null);
        }
        catch (Exception ex)
        {
            VRLog.Warn("Board", $"[Ping] resolving PingManager.Ping3DElementSinglePlayer threw: {ex}");
            _pingSinglePlayer = null;
        }

        if (_pingSinglePlayer == null && !_warnedUnresolved)
        {
            _warnedUnresolved = true;
            VRLog.Warn("Board", "[Ping] PingManager.Ping3DElementSinglePlayer(GameObject) not found — hex ping disabled.");
        }

        return _pingSinglePlayer != null;
    }
}
