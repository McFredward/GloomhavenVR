using System;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Board;

/// <summary>
/// Feature #3: point the LASER at a hex and press the dominant-hand "A" button to
/// fire the game's OWN ping on that hex — a 3D highlight + a floating tooltip.
///
/// MP BUG #7 FIX (2026-08): the first version called
/// <c>PingManager.Ping3DElementSinglePlayer</c>, which is PURELY LOCAL — it never touches
/// the network, so VR pings were invisible to every peer (both directions) and carried no
/// player name. We now drive the exact code path the FLAT game uses for a ping,
/// <c>UIScenarioMultiplayerController.PingTile(CClientTile)</c> (decompiled
/// UIScenarioMultiplayerController.cs:341-353), which
///  - online: shows the ping locally with the OWN player name
///    (<c>Ping3DElementMultiPlayer(tile, PlayerRegistry.MyPlayer)</c>) AND replicates it as the
///    vanilla <c>GameActionType.PingHex</c> side action
///    (<c>Synchronizer.SendSideAction(PingHex, new TileToken(...))</c>). Every peer — modded VR
///    or vanilla flat — renders it through its own <c>PingManager.ProxyPingHex</c> with the
///    sender's username (GameAction.cs:479-482, PingManager.cs:120). Host and client use the
///    SAME path (the flat game's client ping is this very call), so an unassigned joining
///    peer can ping too — vanilla never gates PingHex on turn control or character assignment.
///  - offline: falls through to <c>Ping3DElementSinglePlayer</c> inside PingTile itself.
/// If the controller singleton is not alive (or the method is gone after a game update) we
/// degrade to the old local-only <c>Ping3DElementSinglePlayer</c> so solo pinging keeps working.
///
/// The VR-visible NAME LABEL at the ping marker (the game's own tooltip lives on a
/// screen-space canvas that VR cannot read) is added by <see cref="Patches.PingNameTag_Patch"/>
/// on <c>PingManager.Ping3DElement</c> — it fires for our own pings AND for every ping
/// received from any peer, flat or VR.
///
/// SILENT-GATE FIX (same MP test): every rejection between the A-press and the actual ping
/// call used to be a silent return, which made "the joining peer cannot ping at all"
/// undiagnosable from logs. A press is an explicit user action now: each rejected press logs
/// its reason exactly once (edge-triggered input, so this cannot spam).
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
/// <c>TileBehaviour.m_ClientTile</c> — the exact tile object the game itself pings.
///
/// SHARED SEAM (fingertip ping, 2026-08): <see cref="TryPingClientTile"/> is the ONE mod
/// entry into the game's ping machinery — this class's A-press path and the fingertip-touch
/// ping in <see cref="BoardClickDriver"/> both go through it, so the reflection resolution,
/// the online/offline arbitration and the global anti-spam cooldown exist exactly once.
///
/// All game calls are reflection-guarded: if a method can't be resolved at runtime
/// (game update / stripped build) that path degrades gracefully after one warning rather
/// than throwing every press.
/// </summary>
internal sealed class BoardPing : MonoBehaviour
{
    /// <summary>Minimum gap between pings (seconds, unscaled) — anti-spam on top of the press
    /// edge. STATIC (2026-08): shared between the A-press path and the fingertip ping, so the
    /// two input routes cannot interleave into a faster stream than either alone is allowed.</summary>
    private const float Cooldown = 0.2f;

    private static bool _resolved;
    private static MethodInfo? _pingTile;           // UIScenarioMultiplayerController.PingTile(CClientTile)
    private static MethodInfo? _pingSinglePlayer;   // PingManager.Ping3DElementSinglePlayer(GameObject)
    private static bool _warnedUnresolved;

    private static float _lastPingTime = float.NegativeInfinity;

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

        // From here on the press is an explicit ping ATTEMPT — every rejection is logged
        // (once per press, the edge above makes spam impossible), because the MP test #7
        // failure mode "peer cannot ping at all" was exactly a silent gate in this chain.
        if (!BoardPick.HasHit)
        {
            VRLog.Info("Board", "[Ping] press rejected — the laser/near pick hits nothing " +
                $"(source={BoardPick.Source}, inScenario={BoardPick.InScenario}).");
            return;
        }

        CClientTile? clientTile = ResolveClientTile();
        if (clientTile == null || clientTile.m_GameObject == null)
        {
            VRLog.Info("Board", "[Ping] press rejected — hit collider " +
                $"'{BoardPick.HitCollider?.name}' resolves to no hex tile (no TileBehaviour/m_ClientTile).");
            return;
        }

        TryPingClientTile(clientTile, "laser A-press");
    }

    /// <summary>
    /// THE mod's single entry into the game's ping machinery — used by the A-press path above
    /// and by the outside-selection-phase fingertip ping (<see cref="BoardClickDriver"/>).
    ///
    /// Preferred route: the game's own flat-game ping entry
    /// <c>UIScenarioMultiplayerController.PingTile(CClientTile)</c>, which handles online
    /// (local display with own name + <c>GameActionType.PingHex</c> replication to all peers)
    /// and offline (single-player display) itself — host and client identically, no assignment
    /// required. Fallback: local-only <c>PingManager.Ping3DElementSinglePlayer</c>.
    ///
    /// Returns true only when a ping actually fired (either route) — callers key haptics on
    /// this. The shared <see cref="Cooldown"/> gate is deliberately QUIET (pure anti-spam;
    /// every explicit-press rejection stays logged at the call sites).
    /// </summary>
    internal static bool TryPingClientTile(CClientTile? clientTile, string source)
    {
        if (clientTile == null || clientTile.m_GameObject == null)
            return false;

        if (Time.unscaledTime - _lastPingTime < Cooldown)
            return false; // pure anti-spam, the only intentionally quiet gate

        if (!EnsureResolved())
            return false; // warned once in EnsureResolved

        try
        {
            UIScenarioMultiplayerController? mpc = UIScenarioMultiplayerController.Instance;
            if (mpc != null && _pingTile != null)
            {
                _pingTile.Invoke(mpc, new object[] { clientTile });
                _lastPingTime = Time.unscaledTime;
                VRLog.Info("Board", $"[Ping] pinged hex '{clientTile.m_GameObject.name}' via game " +
                    $"PingTile (replicated to peers when online; source: {source}).");
                return true;
            }

            // Fallback: local-only display (no scenario MP controller alive / game update
            // removed PingTile). Solo behaviour is identical to the original implementation.
            PingManager? manager = PingManager.Instance;
            if (manager == null || _pingSinglePlayer == null)
            {
                VRLog.Warn("Board", $"[Ping] {source} rejected — neither UIScenarioMultiplayerController " +
                    $"nor PingManager is available (mpc={(mpc == null ? "null" : "ok")}, " +
                    $"manager={(manager == null ? "null" : "ok")}).");
                return false;
            }

            _pingSinglePlayer.Invoke(manager, new object[] { clientTile.m_GameObject });
            _lastPingTime = Time.unscaledTime;
            VRLog.Info("Board", $"[Ping] pinged hex '{clientTile.m_GameObject.name}' (LOCAL-ONLY " +
                $"fallback — no UIScenarioMultiplayerController; peers will not see this ping; source: {source}).");
            return true;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Board", $"[Ping] game ping call threw ({source}): {ex}");
            return false;
        }
    }

    /// <summary>
    /// The <c>CClientTile</c> under the current VR pick, or null. Mirrors
    /// <c>BoardPick.ResolveCursorWorld</c>: hit collider → parent <c>TileBehaviour</c> →
    /// <c>m_ClientTile</c> (verified vs GH.Runtime: TileBehaviour.cs:14
    /// <c>public CClientTile m_ClientTile;</c>, CClientTile.cs:7 <c>public GameObject m_GameObject;</c>).
    /// </summary>
    private static CClientTile? ResolveClientTile()
    {
        Collider? collider = BoardPick.HitCollider;
        if (collider == null)
            return null;

        TileBehaviour? tile = collider.GetComponentInParent<TileBehaviour>();
        if (tile == null)
            return null;

        return tile.m_ClientTile;
    }

    /// <summary>
    /// Resolve (once) the publicized game entry points:
    /// <c>UIScenarioMultiplayerController.PingTile(CClientTile)</c> (preferred, networked) and
    /// <c>PingManager.Ping3DElementSinglePlayer(GameObject)</c> (local fallback). Only when BOTH
    /// are missing does the feature disable itself (warn once). Reflection so a renamed/removed
    /// method degrades gracefully instead of throwing a MissingMethodException every press.
    /// </summary>
    private static bool EnsureResolved()
    {
        if (_resolved)
            return _pingTile != null || _pingSinglePlayer != null;

        _resolved = true;
        try
        {
            _pingTile = typeof(UIScenarioMultiplayerController).GetMethod(
                "PingTile",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(CClientTile) },
                modifiers: null);
        }
        catch (Exception ex)
        {
            VRLog.Warn("Board", $"[Ping] resolving UIScenarioMultiplayerController.PingTile threw: {ex}");
            _pingTile = null;
        }

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

        if (_pingTile == null)
            VRLog.Warn("Board", "[Ping] UIScenarioMultiplayerController.PingTile(CClientTile) not found — " +
                "VR pings stay LOCAL-ONLY (no MP replication).");

        if (_pingTile == null && _pingSinglePlayer == null && !_warnedUnresolved)
        {
            _warnedUnresolved = true;
            VRLog.Warn("Board", "[Ping] no ping entry point found (PingTile AND " +
                "Ping3DElementSinglePlayer missing) — hex ping disabled.");
        }

        return _pingTile != null || _pingSinglePlayer != null;
    }
}
