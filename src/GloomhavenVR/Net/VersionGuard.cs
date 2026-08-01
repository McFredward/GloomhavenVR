using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.Net;

/// <summary>
/// Mod-version handshake (joining-client side): every modded peer advertises its
/// <see cref="NetProtocol.ModBuild"/> + display string in the extras extension tail
/// (<see cref="NetProtocol.ExtIdModVersion"/>, at the existing 5 Hz extras rate — no new packet
/// type, no reliability machinery: the record repeats until the peer leaves). This class keeps
/// the per-peer registry, detects a mismatch, and owns the blocking dialog.
///
/// DECISION MODEL — the mismatch is detected and decided LOCALLY, against ANY mismatching
/// modded peer (not only the host): that covers the canonical "we joined, the host mismatches"
/// case AND the flat-host-two-VR-clients case with one rule and no host special-casing
/// (host identity, for the record: FFSNet host is always PlayerID 1 —
/// decompiled PlayerRegistry.cs:37 <c>HostPlayerID = 1</c> / NetworkPlayer.cs:31
/// <c>IsClient =&gt; PlayerID &gt; HostPlayerID</c>). Whether client→client extras relay through a
/// MODLESS host actually works is inferred from the game's own ping side-actions using the
/// same relay — not hardware-verified with a flat host yet.
///
/// MISMATCH := a modded peer whose extras carry a version record with a different ModBuild, OR
/// a modded peer whose extras carry NO record at all (wild builds that predate the handshake —
/// they read as ModBuild 0). A FLAT peer sends no mod traffic whatsoever, is never in the
/// registry, and therefore never a mismatch — no dialog when VR joins a flat host.
///
/// The user's two ways out:
///  * "Als Flat-Spieler joinen" → <see cref="NetSession.FlatNetMode"/> for the rest of the
///    session (all mod net off, VR stays on locally, vanilla MP untouched);
///  * "Abbrechen" → leave the session via the game's OWN leave path
///    (<c>UIMultiplayerEscSubmenu.EndSession()</c> — the exact method the game's leave button
///    confirms into, decompiled UIMultiplayerEscSubmenu.cs:513; never a raw Bolt disconnect).
///
/// TickGuard/phase safety: ticked as its OWN phase in <c>NetAvatarDriver.Update</c> behind its
/// own catch, so nothing here can ever starve TickSend. The whole guard sits behind the
/// <c>[Net] VersionGuard</c> off-switch (default on).
/// </summary>
internal static class VersionGuard
{
    private struct PeerInfo
    {
        /// <summary>An extras packet has been seen — only then is "no version record" meaningful
        /// (a rig-only glimpse of a peer must not read as build 0 for the ≤200 ms until their
        /// first extras arrives).</summary>
        public bool HasExtras;

        /// <summary>The peer's ModBuild; 0 = pre-handshake build (record absent).</summary>
        public ushort Build;

        /// <summary>The peer's display version string (may be null/empty on old builds).</summary>
        public string? Text;
    }

    /// <summary>Every peer we ever saw a valid GVR1 packet from this session — the "modded
    /// peers" registry (also feeds <see cref="PlayerBadges"/>).</summary>
    private static readonly Dictionary<int, PeerInfo> Peers = new();

    /// <summary>Peers whose mismatch the user already answered — the "once per session/peer-set"
    /// gate (never per packet).</summary>
    private static readonly HashSet<int> Handled = new();

    private static readonly VersionDialog Dialog = new();
    private static bool _wasOnline;
    private static bool _mismatchLogged;

    /// <summary>Our own version, formatted once for the dialog/log.</summary>
    private static string OurVersion => $"{MyPluginInfo.PLUGIN_VERSION} (Build {NetProtocol.ModBuild})";

    // ---- registry feed (called from the driver's receive path, main thread) --------------

    /// <summary>Any valid GVR1 packet from <paramref name="senderId"/> proves a modded peer.</summary>
    internal static void NotePacket(int senderId)
    {
        if (senderId > 0 && !Peers.ContainsKey(senderId))
            Peers[senderId] = default;
    }

    /// <summary>An extras packet from a modded peer — the only packet the version record rides.</summary>
    internal static void NoteExtras(int senderId, in PresenceState extras)
    {
        if (senderId <= 0)
            return;
        Peers.TryGetValue(senderId, out PeerInfo info);
        info.HasExtras = true;
        if (extras.HasModVersion)
        {
            info.Build = extras.ModBuild;
            info.Text = extras.ModVersionText;
        }
        // No record on an extras packet ⇒ the peer predates the handshake ⇒ Build stays 0,
        // which mismatches our ModBuild ≥ 1 by definition. Exactly the intended treatment
        // of the wild pre-handshake builds.
        Peers[senderId] = info;
    }

    /// <summary>True when <paramref name="playerId"/> is a known MODDED peer this session
    /// (any valid mod packet seen). Flat players are never in here.</summary>
    internal static bool IsModdedPeer(int playerId) => Peers.ContainsKey(playerId);

    // ---- per-frame ----------------------------------------------------------------------

    internal static void Tick(INetTransport transport)
    {
        bool online = VRSession.IsRunning && transport.IsOnline;
        if (!online)
        {
            // Session ended: everything version-related is session state. Clearing FlatNetMode
            // here is what makes "join as flat" a per-session choice — the next lobby starts
            // with full sync and a fresh handshake.
            if (_wasOnline)
                Reset();
            _wasOnline = false;
            return;
        }
        _wasOnline = true;

        Dialog.Tick();

        if (NetSession.FlatNetMode)
            return;
        if (NetModule.VersionGuardEnabled != null && !NetModule.VersionGuardEnabled.Value)
        {
            // Off-switch flipped mid-session: drop an open dialog, ask nothing further.
            if (Dialog.IsShowing)
                Dialog.Close();
            return;
        }
        if (Dialog.IsShowing)
            return;

        foreach (KeyValuePair<int, PeerInfo> kv in Peers)
        {
            if (!kv.Value.HasExtras || kv.Value.Build == NetProtocol.ModBuild || Handled.Contains(kv.Key))
                continue;
            ShowMismatch(kv.Key, kv.Value);
            break; // one dialog; further mismatching peers are covered by the chosen answer
        }
    }

    internal static void Reset()
    {
        Peers.Clear();
        Handled.Clear();
        Dialog.Close();
        NetSession.Reset();
        _mismatchLogged = false;
    }

    // ---- mismatch dialog ----------------------------------------------------------------

    private static void ShowMismatch(int peerId, in PeerInfo info)
    {
        string peerName = NetPlayerActors.NameFor(peerId) ?? $"#{peerId}";
        string theirs = info.Build > 0
            ? $"{(string.IsNullOrEmpty(info.Text) ? "?" : info.Text)} (Build {info.Build})"
            : Loc.Mod("ver_build_unknown");

        if (!_mismatchLogged)
        {
            _mismatchLogged = true;
            VRLog.Warn("Net", $"MOD VERSION MISMATCH: peer {peerId} ('{peerName}') runs {theirs}, "
                              + $"we run {OurVersion} — showing the join-as-flat/leave dialog.");
        }

        int decidedPeer = peerId;
        Dialog.Show(
            Loc.Mod("ver_mismatch_title"),
            string.Format(Loc.Mod("ver_mismatch_body"), peerName, theirs, OurVersion),
            Loc.Mod("ver_join_flat"),
            Loc.Mod("ver_cancel"),
            onJoinFlat: () =>
            {
                // Mark EVERY currently-mismatching peer handled, not just the trigger — the
                // answer is "this session runs without mod sync", not "ignore that one player".
                foreach (KeyValuePair<int, PeerInfo> kv in Peers)
                {
                    if (kv.Value.HasExtras && kv.Value.Build != NetProtocol.ModBuild)
                        Handled.Add(kv.Key);
                }
                NetSession.FlatNetMode = true;
                VRLog.Info("Net", "FLAT-NET MODE chosen: mod networking OFF for this session "
                                  + "(send + receive gated, remote avatars torn down); VR stays "
                                  + "on locally, vanilla multiplayer untouched.");
            },
            onCancel: () =>
            {
                Handled.Add(decidedPeer);
                VRLog.Info("Net", "Version-mismatch dialog: user chose Abbrechen — leaving the "
                                  + "session via the game's own leave path.");
                LeaveSession();
            });
    }

    // ---- leave path ---------------------------------------------------------------------

    /// <summary>
    /// Leave the multiplayer session exactly the way the game's own "End session" button does:
    /// <c>UIMultiplayerEscSubmenu.EndSession()</c> (voice-chat shutdown + <c>SessionService
    /// .EndSession()</c> → <c>FFSNetwork.Shutdown</c> + UI nav re-enter — decompiled
    /// UIMultiplayerEscSubmenu.cs:513-518; the game itself calls this from non-UI code, e.g.
    /// NetworkBehaviour.cs:106). Fallback when that singleton is not alive: the game's own
    /// <c>FFSNetwork.Shutdown()</c> wrapper via reflection (IProtocolToken sits in a Bolt
    /// assembly the mod deliberately never references) — still never a raw Bolt disconnect.
    /// </summary>
    private static void LeaveSession()
    {
        try
        {
            if (Singleton<UIMultiplayerEscSubmenu>.IsInitialized)
            {
                Singleton<UIMultiplayerEscSubmenu>.Instance.EndSession();
                VRLog.Info("Net", "Left the session via UIMultiplayerEscSubmenu.EndSession().");
                return;
            }
        }
        catch (Exception e)
        {
            VRLog.Warn("Net", $"UIMultiplayerEscSubmenu.EndSession() failed ({e.Message}) — "
                              + "falling back to FFSNetwork.Shutdown().");
        }

        try
        {
            MethodInfo? shutdown = AccessTools.Method("FFSNetwork:Shutdown");
            if (shutdown != null)
            {
                // Shutdown(IProtocolToken clientDisconnectionToken = null, UnityAction onShutdownCompleted = null):
                // as a CLIENT the token is ignored anyway (decompiled FFSNetwork.cs:92-99), so
                // null/null is exactly the client-flavoured leave.
                shutdown.Invoke(null, new object?[] { null, null });
                VRLog.Info("Net", "Left the session via FFSNetwork.Shutdown() (fallback path).");
            }
            else
            {
                VRLog.Error("Net", "Could not leave the session: FFSNetwork.Shutdown not found. "
                                   + "Please leave via the game's own menu.");
            }
        }
        catch (Exception e)
        {
            VRLog.Error("Net", $"FFSNetwork.Shutdown() fallback threw: {e}");
        }
    }
}
