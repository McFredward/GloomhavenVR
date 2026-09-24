using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using FFSNet;              // PlayerRegistry.HostPlayerID — a plain int static, no Bolt type
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

    /// <summary>Change-gate for <see cref="NoteMixedSession"/>: a fold of the roster's player ids
    /// with each one's modded/flat verdict, the host's id and <see cref="NetSession.FlatNetMode"/>.
    /// The census is read once per frame from <see cref="Tick"/>, so it may only print on a
    /// TRANSITION — a join, a leave, a peer's first mod packet, or the flat-net choice.</summary>
    private static int _censusSignature;
    private static bool _censusKnown;

    /// <summary>Reused by <see cref="NoteMixedSession"/> so the per-frame signature fold allocates
    /// nothing in the steady state (the census itself runs at most a handful of times a session,
    /// but the fold that decides whether to run it does not).</summary>
    private static readonly List<(int Id, string? Account, string? Name)> CensusRoster = new();

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

    /// <summary>
    /// The ModBuild <paramref name="playerId"/> is running, or 0 for a FLAT player and for a
    /// modded peer whose version record has not arrived yet.
    ///
    /// <para>WHY A READER WANTS IT (2026-09-06, report item 2): a feature that needs a record the
    /// PEER has to send cannot be told apart, from the receiving side, into "they chose not to
    /// send it" and "their build cannot send it" — and those are different problems with different
    /// answers, one of which is "ask them to update". A surface that degrades because of the
    /// sender's build must be able to SAY so rather than degrade silently, which is this project's
    /// standing rule for disabling a feature.</para>
    /// </summary>
    internal static int PeerBuild(int playerId) =>
        Peers.TryGetValue(playerId, out PeerInfo info) ? info.Build : 0;

    /// <summary>Connected compatible participants, independent of transient avatar loading.
    /// The native roster is refreshed by the ordinary session census. A slow scene load must
    /// not make an existing participant ineligible for a completed story or reward.</summary>
    internal static void CollectContinuationPeers(List<int> into, int localPlayerId)
    {
        into.Clear();
        for (int i = 0; i < CensusRoster.Count; i++)
        {
            int id = CensusRoster[i].Id;
            if (id > 0 && id != localPlayerId && PeerBuild(id) == NetProtocol.ModBuild)
                into.Add(id);
        }
    }

    /// <summary>
    /// WHICH MULTIPLAYER SESSION THIS IS, as a number that changes when one ends.
    ///
    /// <para>Bumped by <see cref="Reset"/>, i.e. exactly when the peer registry is cleared. A
    /// once-per-session verdict that is change-gated on its own value alone goes SILENT in the
    /// second session of a process whenever the answer happens to repeat — the same class of
    /// held-instrument defect this project has already paid for. Folding this int into such a gate
    /// fixes that with one static read and no reset call reaching back into this class, so the
    /// dependency stays one-directional.</para>
    /// </summary>
    internal static int SessionEpoch { get; private set; }

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

        // BEFORE the dialog and before BOTH early returns below, on purpose. This is not part of
        // the guard's DECISION — it decides nothing and shows nothing — it is the log's answer to
        // "who am I actually playing with", and the two states in which that answer matters most
        // are exactly the two the returns below take: flat-net mode, and the off-switch. An
        // instrument that goes quiet in the session it exists to describe is the failure this
        // project keeps paying for.
        NoteMixedSession();

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
        CensusRoster.Clear();
        Handled.Clear();
        Dialog.Close();
        NetSession.Reset();
        _mismatchLogged = false;
        _censusKnown = false;
        _censusSignature = 0;
        unchecked { SessionEpoch++; }
    }

    // ---- the mixed-session census -------------------------------------------------------

    /// <summary>
    /// WHO IS ACTUALLY IN THIS SESSION, AND WHAT THAT MEANS FOR THE TWO FEATURES THAT DEPEND ON
    /// THE HOST BEING MODDED.
    ///
    /// <para><b>WHY IT EXISTS.</b> Playing with FLAT players — players without the mod — is a
    /// standing requirement, and both host-dependent features degrade to the flat game's behaviour
    /// when the host is one: <see cref="EncounterChoice.MayUnlock"/> leaves the game's own client
    /// lock in place, and <see cref="EnemyInfoContinue"/> does not offer the 'Fortfahren' cap. Both
    /// of those are SILENCES. The encounter's refusal line only prints for a press that got as far
    /// as <c>OnLocalPress</c>, which a locked button never does; the reveal's DISARMED line is
    /// change-gated, and against a flat host the arm state never changes at all. So a session with
    /// a flat host used to produce no statement anywhere about why either control was missing, and
    /// "the gate is correct" was indistinguishable from "the gate never ran".</para>
    ///
    /// <para><b>IT DECIDES NOTHING.</b> The verdicts printed here are read off
    /// <see cref="IsModdedPeer"/> and <see cref="EncounterChoice.HostCanHonourRequests"/>, the same
    /// terms the features themselves ask; those verdicts do not depend on
    /// the log emission. The refreshed roster also scopes native continuation recipients.
    /// A peer is MODDED from the first valid GVR1 packet
    /// (<see cref="NotePacket"/>), so a peer named FLAT here is one nothing has ever arrived
    /// from.</para>
    ///
    /// <para>Change-gated on <see cref="_censusSignature"/>: at most one line per join, per leave,
    /// per first-packet, and per flat-net choice.</para>
    /// </summary>
    private static void NoteMixedSession()
    {
        CensusRoster.Clear();
        NetPlayerActors.CollectRoster(CensusRoster);
        if (CensusRoster.Count == 0)
            return;   // the registry is not readable yet; say nothing rather than say "solo"

        int hostId = PlayerRegistry.HostPlayerID;
        bool flatNet = NetSession.FlatNetMode;

        int signature = flatNet ? 17 : 3;
        int flatCount = 0;
        for (int i = 0; i < CensusRoster.Count; i++)
        {
            int id = CensusRoster[i].Id;
            bool modded = IsModdedPeer(id) || id == NetPlayerActors.LocalPlayerId();
            if (!modded)
                flatCount++;
            unchecked { signature = signature * 31 + (id * 2 + (modded ? 1 : 0)); }
        }
        if (_censusKnown && signature == _censusSignature)
            return;
        _censusKnown = true;
        _censusSignature = signature;

        var who = new StringBuilder();
        int localId = NetPlayerActors.LocalPlayerId();
        for (int i = 0; i < CensusRoster.Count; i++)
        {
            (int Id, string? Account, string? Name) row = CensusRoster[i];
            bool isLocal = row.Id == localId && localId > 0;
            bool modded = isLocal || IsModdedPeer(row.Id);
            Peers.TryGetValue(row.Id, out PeerInfo info);
            who.Append(" · player ").Append(row.Id)
               .Append(" '").Append(string.IsNullOrEmpty(row.Name) ? "?" : row.Name).Append('\'')
               .Append(row.Id == hostId ? " (HOST)" : string.Empty)
               .Append(isLocal ? " (THIS CLIENT)" : string.Empty)
               .Append(modded
                    ? isLocal
                        ? $" = MODDED, {OurVersion}"
                        : info.Build > 0
                            ? $" = MODDED, Build {info.Build}"
                            : " = MODDED (a GVR1 packet has arrived, no version record yet)"
                    : " = FLAT — no GVR1 packet has EVER arrived from this player, so they are "
                      + "running the unmodded game");
        }

        bool hostHonours = EncounterChoice.HostCanHonourRequests();

        // HW-VERIFY: the answer to "what happens when I play with flat players", printed by the
        // session itself instead of inferred afterwards. FALSIFIER: in a MODDED-ONLY session every
        // row must read MODDED and the consequence clause must read OFFERED — if a row there says
        // FLAT, the handshake is not arriving and the two features are off for a reason that is a
        // BUG rather than a flat player. The line's absence while `] [Net] RX ` lines are present
        // means the census never ran at all, which is a different defect from a wrong verdict.
        VRLog.Note("Net", $"MIXED SESSION CENSUS: {CensusRoster.Count} player(s), "
                        + $"{flatCount} of them FLAT.{who} — "
                        + (hostHonours
                            ? "THE HOST RUNS THE MOD, so this client's encounter option buttons are "
                              + "UNLOCKED and the enemy-information 'Fortfahren' cap is OFFERED; "
                              + "both presses travel to the host as side actions and the host "
                              + "presses its own button."
                            : flatNet
                                ? "NetSession.FlatNetMode — this player chose to join as a flat "
                                  + "player, so every mod net path is off for the rest of the "
                                  + "session and BOTH host-dependent features are off with it."
                                : "THE HOST IS NOT A MODDED PEER, so BOTH host-dependent features "
                                  + "are OFF on this client BY DESIGN: the encounter option buttons "
                                  + "keep the game's own client lock (an unmodded host would read "
                                  + "the request's unset SupplementaryDataIDMed as option 0 and "
                                  + "throw into FFSNetwork.HandleDesync), and the 'Fortfahren' cap "
                                  + "is not drawn. The host presses; everyone follows. This is the "
                                  + "flat game's behaviour and it is not a stall.")
                        + " WHAT A FLAT PLAYER COSTS, STATED RATHER THAN HIDDEN: they publish no "
                        + "mod records at all, so for them there is no VR avatar, no mirrored "
                        + "control board, no shared window pose and no shared story page — they "
                        + "click their own story dialog through at their own pace, exactly as the "
                        + "unmodded game does. NOTHING ON THIS CLIENT WAITS FOR A PACKET THEY "
                        + "CANNOT SEND: every follow-the-peer path here is adopt-if-published with "
                        + "a local default, never a precondition.");
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
