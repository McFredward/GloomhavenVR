using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

// =================================================================================================
//  DEBUG TEST-TRIGGER OVERRIDE — extension record 32, the whole of it: sampler, consumer, ownership
//  and teardown. The wire bytes themselves live in PresenceState/NetProtocol; everything about WHO
//  the override belongs to and WHEN it is released is here.
// =================================================================================================

/// <summary>
/// Makes the Erweitert ▸ Test-Auslöser page's latches VISIBLE TO EVERY PLAYER instead of only to the
/// tester who pressed them.
///
/// <para><b>USER RULING, 2026-08-15 (verbatim):</b> "Auch wenn jemand im Debugmenu ein Event startet
/// sollte dies auch von ALLEN im Multiplayer sichtbar sein statt nur lokal, also synchronisiert
/// werden."</para>
///
/// <para><b>THIS REVERSES A DECISION THAT WAS ARGUED IN THE CODE, so read what it replaced.</b> Both
/// overrides shipped as deliberately local test aids, and both said so in their log lines: "LOCAL
/// TEST AID ONLY: a haunt is not game state and not on the wire — a peer keeps computing the real
/// schedule from the same shared clock and is unaffected. Only this headset draws differently."
/// Every clause of that is still TRUE — a haunt still is not game state, the element force is still
/// applied between sensing and publishing so the game's element board (a desync invariant it
/// compares every round) is still read and never written, and this record still cannot make two
/// clients disagree about anything the game checks. What was wrong was the CONCLUSION: "it cannot
/// desync the game" is not an argument that a press should be invisible, and the user has now said
/// which of the two he wants.</para>
///
/// <para><b>WHAT GOES ON THE WIRE: THE LATCH, NEVER THE ANIMATION.</b> Both subsystems are pure
/// functions of the shared environment clock (<see cref="SkyAlternative.EnvClockSeconds"/>, already
/// synchronised by extension record 31 and its owner election). So a receiver only has to learn
/// WHICH override is standing; it then evaluates that against its own copy of the same clock and
/// arrives at the same picture, frame for frame. Eight bytes at ≤5 Hz while a latch stands, and not
/// one byte in any other second of any other session.</para>
///
/// <para><b>WHO WINS WHEN TWO PLAYERS FORCE DIFFERENT THINGS: LAST WRITER WINS, and a press MUTATES
/// the shared set rather than replacing it.</b> There is exactly ONE override set in a session. The
/// client whose set most recently CHANGED owns it and is the only one that transmits; everybody else
/// — including a player who pressed a minute ago — follows. When a follower presses a button, the
/// press lands on the set it is already showing (its own latches were driven there by the owner), so
/// the result is the owner's set PLUS that press, and the presser becomes the new owner. That is why
/// two testers can build an element MIXTURE together, which is exactly what the element half of the
/// page was asked for in the first place ("So kann ich die Mischungen besser testen").
/// <br/>The one case that is genuinely arbitrary is two presses that cross in flight, inside one
/// packet interval: then the later-arriving SET wins wholesale on each client and the other press is
/// lost. That is stated rather than hidden, it is logged when it happens, and it is the price of not
/// interleaving two overrides into a state neither tester asked for. It converges: the loser can see
/// what stands on the row captions and press again.</para>
///
/// <para><b>THE LOCAL VIEW IS NEVER ROUTED THROUGH THE NETWORK.</b> The button calls
/// <c>Haunt.Force</c> / <c>ElementMood.Force</c> directly and the environment answers on the same
/// frame; <see cref="NoteLocalPress"/> only makes the presser the OWNER, which is what causes the
/// press to be transmitted. A tester alone in a session sees exactly what they saw before this
/// record existed, on exactly the same frame.</para>
///
/// <para><b>LOCAL SETTINGS ARE A FILTER ON THE SYNCED STATE, NEVER THE OTHER WAY ROUND — the ruling
/// that governs every line of <see cref="Drive"/>. USER RULING, 2026-08-15 (verbatim):</b> "Die
/// Events können verständlicherweise nur syncen, wenn die Spieler die selbe Umgebung eingestellt
/// haben. Das soll lokal eingestellt sein. Genauso, wenn lokal der Spieler Events oder Elemente
/// ausgeschaltet hat sieht er sie auch nicht. D.h. die lokalen Einstellungen haben Vorrang. Sind die
/// Spieler aber in der selben Umgebung und haben die Events oder Element-Effekte eingeschaltet sollen
/// all diese Spieler die Effekte zusammen gleichzeitig gesynced erleben."</para>
///
/// <para>The wire carries the SCHEDULE OVERRIDE; the local settings carry the PERMISSIONS. The full
/// five-point contract is written once, at <see cref="NetProtocol.ExtIdTestForce"/>; what it means
/// HERE is that <see cref="Drive"/> is the ONLY place a peer's override can enter this client, and
/// it refuses at the door rather than letting something in and hoping a gate downstream catches it:
/// <list type="bullet">
/// <item><b>The environment must match</b> — the receiver's own <c>[Sky] Style</c> DIAL against the
/// sender's. A mismatch is a silent no-op, and by the same code path a RELEASE of anything that
/// sender had previously applied here (which is what makes an owner who switches environment
/// mid-latch safe). A player in the cellar while another is in the forest is a legitimate state.</item>
/// <item><b><c>[Haunt] EasterEggs</c> off ⇒ no apparition enters</b>, whatever a peer forced. The
/// LOCAL force still overrides the local switch — that is the debug aid, on the tester's own headset,
/// by their own press, and <c>Haunt.Tick</c>'s <c>!Forcing</c> clause is what implements it. A PEER's
/// force must not, and it cannot, because it never becomes a latch here at all.</item>
/// <item><b><c>[Elements] EnvironmentResponse</c> off ⇒ no element force enters</b>, same rule.
/// <c>[Elements] ResponseStrength</c> is NOT a permission and is deliberately not consulted: it is a
/// magnitude the player chose and it already scales a synced force exactly as it scales a real
/// infusion.</item>
/// </list>
/// A client with a switch off IS NOT DESYNCED and is never logged as one — it has exercised a
/// setting, which is what a setting is for.</para>
///
/// <para><b>THE APPARITION HALF NEEDS ONE GATE MORE THAN THE ELEMENT HALF</b>, and it is a room
/// rather than a permission: <see cref="SkyAlternative.WireStyleCode"/> must be non-zero locally,
/// i.e. a haunted shell is really standing here. <c>Haunt.ForceReady</c> reads the DIAL and is
/// therefore true under mixed reality, where <c>Haunt.Tick</c> never runs and
/// <c>SkyAlternative.StandDown</c> clears the latch every frame — applying there would be a
/// latch/clear loop, one pair of log lines per frame, for a picture that cannot be drawn over
/// passthrough anyway (the standing MR ruling: the mod puts no occluding geometry over the room).
/// The element half has no such gate and must not: the element mood deliberately keeps publishing
/// under passthrough, because a mood is a number and an apparition is a surface.</para>
///
/// <para><b>THE OVERRIDE CANNOT STICK.</b> Four independent routes end it, and only the last is a
/// timeout:
/// <list type="number">
/// <item>the owner presses the row again, or the page's "alles aus" row — the set goes empty and the
/// owner transmits an ALL-ZERO record for <see cref="ReleaseBurstSeconds"/>. That is an EXPLICIT
/// release: every receiver drops the override on the packet, not on a clock;</item>
/// <item>the owner's environment or scenario tears down — <c>Haunt.StandDown</c> and
/// <c>ElementMood.StandDown</c> clear every latch before their own idempotence guard, so the owner's
/// sampled set empties and route 1 runs on its own. A scenario change therefore cannot carry an
/// override into the next scenario, on any client;</item>
/// <item>the owner LEAVES — <see cref="ForgetPeer"/> is called from the driver's player-left and
/// staleness paths, and the loss of the owner releases immediately;</item>
/// <item>and, as the backstop for a crash or a dropped-packet gap, <see cref="StaleSeconds"/> of
/// silence from the owner.</item>
/// </list></para>
///
/// <para><b>A FOLLOWER NEVER REBROADCASTS.</b> Only the owner writes the record. Without that rule
/// every client would echo the override, an owner leaving would leave three copies of their latch
/// alive in each other's echoes, and there would be nothing left to release.</para>
/// </summary>
/// <remarks>CLASSIFICATION: GLOBAL — a shared debug override, ON the wire as extension record 32
/// since 2026-08-15 (it was LOCAL before that, by an explicit decision the ruling above reverses).
/// It is NOT game state: the game's element board is read and never written, and a haunt has no
/// state at all. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal static class RemoteTestTriggers
{
    /// <summary>The six real elements, the game's own <c>EElement</c> order. Matches the six defined
    /// bits of <see cref="NetProtocol.TestForceElementMask"/>.</summary>
    private const int Elements = 6;

    /// <summary>How long the owner keeps transmitting an ALL-ZERO record after its last latch goes.
    /// The extras stream is UNRELIABLE, so a single release packet can be lost — and losing it would
    /// leave every peer holding an override until the staleness backstop fires seconds later. At
    /// ≤5 Hz this is half a dozen chances to be heard, which costs 10 bytes per packet for a second
    /// and a half, once per test session.</summary>
    private const float ReleaseBurstSeconds = 1.5f;

    /// <summary>Seconds of silence after which the owner's override is treated as gone. Deliberately
    /// the same number the environment clock uses for the same reason: extras go out at ≤5 Hz, so it
    /// is a dozen missed packets — long enough that a hitch cannot drop a latch mid-test, short
    /// enough that a crashed owner does not leave the room haunted.
    ///
    /// <para>IT IS THE BACKSTOP AND NOT THE MECHANISM. Every ordinary end of an override — the
    /// button, the stop row, a stand-down, a player leaving — is explicit and arrives long before
    /// this.</para></summary>
    private const float StaleSeconds = 3f;

    /// <summary>Below this, two apparition anchors are the same anchor. Two clients quantize the
    /// same press time through milliseconds, so the difference is either zero or a real re-press;
    /// this only keeps a rounding edge from restarting an apparition.</summary>
    private const float AnchorEpsilon = 0.002f;

    /// <summary>
    /// The whole override, as one comparable value. It is exactly the record's payload, which is
    /// what makes "has this changed" a single struct compare on both the send and the receive side.
    /// </summary>
    private readonly struct Set
    {
        internal Set(byte style, byte hauntCode, byte strong, byte waning, uint hauntSinceMillis)
        {
            Style = style;
            HauntCode = hauntCode;
            Strong = strong;
            Waning = waning;
            HauntSinceMillis = hauntSinceMillis;
        }

        internal readonly byte Style;

        /// <summary>Forced apparition card index PLUS ONE; 0 = none.</summary>
        internal readonly byte HauntCode;

        internal readonly byte Strong;
        internal readonly byte Waning;
        internal readonly uint HauntSinceMillis;

        internal bool IsEmpty => HauntCode == 0 && Strong == 0 && Waning == 0;

        internal bool Same(in Set o) =>
            Style == o.Style && HauntCode == o.HauntCode && Strong == o.Strong
            && Waning == o.Waning && HauntSinceMillis == o.HauntSinceMillis;

        internal string Describe()
        {
            if (IsEmpty)
                return "nothing latched";
            var sb = new System.Text.StringBuilder(64);
            if (HauntCode > 0)
                sb.Append("apparition ").Append(HauntCode - 1).Append(" of style ").Append(Style)
                  .Append(" pressed at ").Append(HauntSinceMillis / 1000f).Append('s');
            for (int i = 0; i < Elements; i++)
            {
                bool strong = (Strong & (1 << i)) != 0;
                bool waning = (Waning & (1 << i)) != 0;
                if (!strong && !waning)
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append((ElementInfusionBoardManager.EElement)i)
                  .Append('=').Append(strong ? "Strong" : "Waning");
            }
            return sb.ToString();
        }

        internal static readonly Set Empty = new(0, 0, 0, 0, 0u);
    }

    /// <summary>A peer's last received override and when it arrived.</summary>
    private readonly struct PeerSet
    {
        internal PeerSet(in Set set, float at, float changedAt)
        {
            Value = set;
            At = at;
            ChangedAt = changedAt;
        }

        internal readonly Set Value;

        /// <summary>Unscaled arrival time — the staleness clock, which must keep running when the
        /// game's own time does not.</summary>
        internal readonly float At;

        /// <summary>Unscaled time at which this peer's override last DIFFERED from the one before
        /// it. This is the last-writer-wins key; it is deliberately a LOCAL arrival stamp and not a
        /// sender-side timestamp, because the only clock all senders share is the environment clock,
        /// which is only actually shared while everyone stands in the same haunted room — precisely
        /// the case this rule must also settle when they do not.</summary>
        internal readonly float ChangedAt;
    }

    private static readonly Dictionary<int, PeerSet> Peers = new();

    /// <summary>Unscaled time of the local player's last press, or 0 while the local player is not
    /// a candidate for ownership at all. Set only by <see cref="NoteLocalPress"/>.</summary>
    private static float _localChangedAt;

    /// <summary>True while a REMOTE peer owns the override and this client is driving its own
    /// channels from that peer's record. It is what makes "the owner went away" releasable: without
    /// it, an empty desired set would be indistinguishable from "this client never followed anyone"
    /// and would clear a local tester's own latches.</summary>
    private static bool _following;

    /// <summary>The local override as it was last sampled, so a set that empties — for ANY reason,
    /// a button or a scenario teardown — starts the explicit release burst.</summary>
    private static Set _lastLocal = Set.Empty;

    /// <summary>Unscaled time until which an ALL-ZERO record keeps being transmitted.</summary>
    private static float _releaseUntil;

    /// <summary>The payload the last extras packet carried, so a change pre-empts the extras
    /// cadence gate instead of waiting up to 200 ms for it.</summary>
    private static Set _lastSent = Set.Empty;
    private static bool _sentAnything;

    // ---- diagnostics: one line per real change, never per frame ---------------------------------
    private static int _loggedOwner = int.MinValue;
    private static Set _loggedSet = Set.Empty;

    /// <summary>Forget everything (session end / flat-net mode). Deliberately does NOT clear the
    /// local channels: a lone player's own test latch is theirs and survives the session ending,
    /// exactly as it did before this record existed.</summary>
    internal static void Reset()
    {
        Peers.Clear();
        _localChangedAt = 0f;
        _following = false;
        _lastLocal = Set.Empty;
        _releaseUntil = 0f;
        _lastSent = Set.Empty;
        _sentAnything = false;
        _loggedOwner = int.MinValue;
        _loggedSet = Set.Empty;
    }

    /// <summary>
    /// A peer left, or went stale enough for the driver to tear their avatar down. Their override
    /// goes with them — this is the EXPLICIT half of "the forcing player disconnects", and it is
    /// what makes <see cref="StaleSeconds"/> a backstop rather than the mechanism.
    /// </summary>
    internal static void ForgetPeer(int playerId)
    {
        if (!Peers.Remove(playerId))
            return;
        VRLog.Info("Net", $"TEST TRIGGER SYNC: player {playerId} is gone and their debug override goes "
                          + "with them. If they owned it, every client releases on the next frame — a "
                          + "debug latch may never outlive the person holding it.");
    }

    /// <summary>
    /// The local tester just pressed a row on the Erweitert test page (or its "alles aus" row). The
    /// press has ALREADY been applied to the local channels by the caller — this only makes the
    /// presser the OWNER, which is what puts it on the wire.
    ///
    /// <para>Calling it after an inert press (one <c>Force</c> refused because there is no scenario
    /// board) is harmless: the sampled set is then unchanged, so nothing new is transmitted and the
    /// only effect is that this client is the one who would transmit if it did change.</para>
    /// </summary>
    /// <param name="what">Named in the one log line this can emit, so a reader can tell which press
    /// took the override away from a peer.</param>
    internal static void NoteLocalPress(string what)
    {
        int previous = _following ? _loggedOwner : 0;
        _localChangedAt = Time.unscaledTime;

        // WE ARE NO LONGER FOLLOWING, and the latches the peer drove into this client are NOT
        // released: they are now part of OUR set and we broadcast them. That is what makes a press a
        // MUTATION of the shared override rather than a replacement of it, which is the whole reason
        // two testers can build an element mixture together.
        _following = false;
        _releaseUntil = 0f;

        // FORGET WHAT WE LAST TRANSMITTED. While a peer owned the override this client sent nothing,
        // so its peers' idea of "what this client is saying" is stale by however long that lasted —
        // and if the press happens to reproduce the set we last owned, the change detector would
        // otherwise decide there was nothing to send.
        _sentAnything = false;

        if (previous > 0)
            VRLog.Info("Net", $"TEST TRIGGER SYNC: local press ({what}) takes the debug override over "
                              + $"from player {previous} — LAST WRITER WINS, and the press lands ON "
                              + "their set rather than replacing it, so whatever they had latched "
                              + "stays latched and this press is added to it. This client transmits "
                              + "the override from now on; they become followers.");
    }

    /// <summary>
    /// Fill <paramref name="extras"/>'s record-32 fields — ONLY while this client owns the override.
    /// A follower writes nothing at all, which is what keeps an override from being echoed by every
    /// client in the room (and therefore releasable by none of them).
    /// </summary>
    internal static void Sample(ref PresenceState extras)
    {
        // _localChangedAt IS the ownership flag on this side: <see cref="Resolve"/> zeroes it the
        // moment a peer's set beats ours, so "we have an unbeaten press" and "we are the owner" are
        // the same test and there is no second flag for the two to disagree about.
        if (_localChangedAt <= 0f || !Bound)
            return;

        Set live = ReadLocal();
        if (live.IsEmpty && Time.unscaledTime >= _releaseUntil)
            return; // nothing latched and the release has been stated — back to zero bytes

        extras.HasTestForce = true;
        extras.TestForceStyle = live.Style;
        extras.TestForceHauntCode = live.HauntCode;
        extras.TestForceStrongMask = live.Strong;
        extras.TestForceWaningMask = live.Waning;
        extras.TestForceHauntSinceMillis = live.HauntSinceMillis;
        _lastSent = live;
        _sentAnything = true;
    }

    /// <summary>
    /// Whether the extras packet must go out NOW rather than on its own cadence. A debug press is a
    /// discrete, human-paced act whose whole purpose is to be looked at, so up to 200 ms of cadence
    /// latency between two headsets is exactly the kind of "did it work?" the page exists to remove.
    /// Also true throughout the release burst, so the ALL-ZERO record is repeated on every packet of
    /// it rather than once.
    /// </summary>
    internal static bool SendDue
    {
        get
        {
            if (_localChangedAt <= 0f || !Bound)
                return false;
            if (Time.unscaledTime < _releaseUntil)
                return true;
            Set live = ReadLocal();
            if (live.IsEmpty)
                return false;
            return !_sentAnything || !live.Same(in _lastSent);
        }
    }

    /// <summary>
    /// Take delivery of one peer's extras packet. A packet WITHOUT the record forgets that peer's
    /// entry, which is what a player who is holding nothing transmits and what a peer predating the
    /// record transmits — and "forgotten" is exactly "has no override to share".
    /// </summary>
    internal static void ApplyPeer(int playerId, in PresenceState p)
    {
        if (!p.HasTestForce)
        {
            Peers.Remove(playerId);
            return;
        }

        var incoming = new Set(p.TestForceStyle, p.TestForceHauntCode, p.TestForceStrongMask,
                               p.TestForceWaningMask, p.TestForceHauntSinceMillis);
        float now = Time.unscaledTime;
        float changedAt = Peers.TryGetValue(playerId, out PeerSet had) && had.Value.Same(in incoming)
            ? had.ChangedAt
            : now;
        Peers[playerId] = new PeerSet(in incoming, now, changedAt);
    }

    /// <summary>
    /// Elect the override's owner and drive this client's channels from it. Runs once per frame over
    /// a dictionary that is EMPTY in single player and in every session in which nobody has opened
    /// the debug page — which is the cost of this feature in every ordinary game.
    ///
    /// <para>THE RULE: the client whose override last CHANGED owns it, ties broken by the lowest
    /// player id so that two clients cannot reach different answers from the same arrivals. Every
    /// client evaluates it over the same set of records and therefore converges within one packet of
    /// the last press.</para>
    /// </summary>
    /// <param name="localPlayerId">This client's player id — the tie-break key, and never an
    /// authority: there is no host concept here.</param>
    internal static void Resolve(int localPlayerId)
    {
        // THE DIALS MAY NOT EXIST YET. All four config entries are bound together by
        // Rig.RenderQuality.Bind and are null! until it has run; a packet can arrive during a scene
        // load, and every read below — the style key, both permissions — would NRE. There is nothing
        // to drive before the settings exist anyway, so this is a wait rather than a failure.
        if (!Bound)
            return;

        float now = Time.unscaledTime;

        // ---- the local candidate: its set, and the release burst it starts when it empties ------
        Set live = ReadLocal();
        if (_localChangedAt > 0f)
        {
            if (live.IsEmpty && !_lastLocal.IsEmpty)
            {
                // The last latch went — by a button, by the stop row, or by a stand-down (scenario
                // end, VR teardown, mixed reality, a style change: both StandDown implementations
                // clear the force BEFORE their own idempotence guard). Say so on the wire rather
                // than letting every peer wait out the staleness backstop.
                _releaseUntil = now + ReleaseBurstSeconds;
                VRLog.Info("Net", "TEST TRIGGER SYNC: the local debug override is empty again — "
                                  + $"transmitting an ALL-ZERO record 32 for {ReleaseBurstSeconds:F1}s "
                                  + "so every peer releases on a PACKET rather than on a timeout, then "
                                  + "back to zero bytes. This runs for a button press and for a "
                                  + "teardown alike, which is why an override cannot survive a "
                                  + "scenario change on anyone's client.");
            }
            if (live.IsEmpty && now >= _releaseUntil)
                _localChangedAt = 0f; // released and stated — stop being a candidate
        }
        _lastLocal = live;

        // ---- elect ------------------------------------------------------------------------------
        int owner = _localChangedAt > 0f ? 0 : -1;   // 0 = us, -1 = nobody
        float bestAt = _localChangedAt;
        int bestId = localPlayerId;
        Set winning = live;

        List<int>? drop = null;
        foreach (KeyValuePair<int, PeerSet> kv in Peers)
        {
            if (now - kv.Value.At > StaleSeconds)
            {
                (drop ??= new List<int>()).Add(kv.Key);
                continue;
            }
            // Strictly later wins; an exact tie falls to the lower player id, so that two clients
            // reading the same two records cannot disagree.
            if (owner != -1
                && (kv.Value.ChangedAt < bestAt
                    || (kv.Value.ChangedAt == bestAt && kv.Key >= bestId)))
                continue;
            owner = kv.Key;
            bestAt = kv.Value.ChangedAt;
            bestId = kv.Key;
            winning = kv.Value.Value;
        }
        if (drop != null)
        {
            for (int i = 0; i < drop.Count; i++)
            {
                Peers.Remove(drop[i]);
                VRLog.Info("Net", $"TEST TRIGGER SYNC: player {drop[i]}'s debug override went silent "
                                  + $"for {StaleSeconds:F0}s and is dropped. This is the BACKSTOP, not "
                                  + "the mechanism — a button, a stop row, a teardown and a player "
                                  + "leaving all release explicitly and long before this.");
            }
        }

        // ---- drive --------------------------------------------------------------------------
        if (owner > 0)
        {
            // WE LOST, SO WE STOP BEING A CANDIDATE AT ALL. Without this, a client that pressed a
            // minute ago would keep <see cref="Sample"/> transmitting its (now peer-driven) latches,
            // every client would echo every override, and an owner leaving would leave three copies
            // of their latch alive in each other's echoes with nothing left to release it. Our old
            // press is not a claim any more; the next one will be.
            _localChangedAt = 0f;
            _releaseUntil = 0f;
            _following = true;
            Drive(in winning, $"player {owner} owns the debug override");
        }
        else if (_following)
        {
            // The owner is gone (left, went silent, or released) and nobody replaced them. Release
            // what THEY put on this client — never a local tester's own latches, which is exactly
            // what _following distinguishes.
            _following = false;
            Drive(in Set.Empty, "the peer that owned the debug override released it or went away");
        }

        // ---- one line per real change -----------------------------------------------------------
        if (owner == _loggedOwner && winning.Same(in _loggedSet))
            return;
        int previousOwner = _loggedOwner;
        _loggedOwner = owner;
        _loggedSet = winning;
        VRLog.Info("Net", "TEST TRIGGER SYNC: "
                          + (owner == -1
                                 ? "no debug override stands anywhere in this session."
                                 : (owner == 0
                                        ? $"this client (player {localPlayerId}) owns the debug override"
                                        : $"player {owner} owns the debug override")
                                   + " — " + winning.Describe() + ".")
                          + (previousOwner != int.MinValue && previousOwner != owner && owner != -1
                                 ? $" It changed hands from {(previousOwner == 0 ? "this client" : "player " + previousOwner)}"
                                   + ", by LAST WRITER WINS: there is one shared override set and the "
                                   + "client that last changed it transmits it."
                                 : string.Empty)
                          + " LOCAL SETTINGS HAVE PRECEDENCE: it is applied here only while this "
                          + $"client's own environment dial is '{SkyAlternative.Style.Value}' like the "
                          + "sender's, and only for the halves this client has switched on — "
                          + $"EasterEggs {(Haunt.EasterEggs.Value ? "on" : "OFF, so no apparition enters")}"
                          + ", EnvironmentResponse "
                          + $"{(ElementMood.EnvironmentResponse.Value ? "on" : "OFF, so no element force enters")}"
                          + ". A client with a switch off is NOT desynced and is not corrected. "
                          + "Nothing here is game state either: the game's element board is still read "
                          + "and never written, so the end-of-round desync compare has nothing to "
                          + "disagree about.");
    }

    /// <summary>
    /// This client's live override, read back out of the two channels themselves rather than out of
    /// a cache. Reading the truth is what makes every apply below idempotent and self-healing: a
    /// latch that a stand-down dropped, or one a <c>Force</c> refused, simply is not in here.
    /// </summary>
    /// <summary>Whether the four config entries this file reads have been bound yet. They are bound
    /// together (<c>Rig.RenderQuality.Bind</c>), so one check covers all of them; before that there is
    /// no setting to filter by and nothing to transmit.</summary>
    private static bool Bound =>
        SkyAlternative.Style != null && Haunt.EasterEggs != null
        && ElementMood.EnvironmentResponse != null;

    private static Set ReadLocal()
    {
        byte strong = 0;
        byte waning = 0;
        for (int i = 0; i < Elements; i++)
        {
            if (ElementMood.IsForced(i, waning: false))
                strong |= (byte)(1 << i);
            else if (ElementMood.IsForced(i, waning: true))
                waning |= (byte)(1 << i);
        }

        // THE STYLE IS THE PLAYER'S DIAL, not record 31's "an animated shell is standing" key: the
        // user's condition is "die selbe Umgebung eingestellt", which is about the SETTING. Two
        // players both on Default are in the same environment; a player in mixed reality still
        // matches their own dial, which is what keeps a peer's element force reaching them.
        byte style = (byte)SkyAlternative.Style.Value;
        if (style > NetProtocol.TestForceMaxStyleCode)
            style = NetProtocol.TestForceStyleUnknown;
        int id = Haunt.ForcedId;
        byte haunt = id >= 0 && id < NetProtocol.TestForceMaxHauntCode ? (byte)(id + 1) : (byte)0;

        uint since = 0u;
        if (haunt != 0)
        {
            double s = Haunt.ForcedLatchedAt;
            if (s > 0d)
                since = (uint)((long)System.Math.Round(s * 1000d) & 0xFFFFFFFFL);
        }
        return new Set(style, haunt, strong, waning, since);
    }

    /// <summary>
    /// Reconcile the local channels with <paramref name="want"/>. Every decision compares against
    /// the LIVE state, so this is idempotent by construction — which matters because it runs every
    /// frame while a peer owns the override, and because <c>Haunt.Force</c> / <c>ElementMood.Force</c>
    /// are TOGGLES: calling one with a state that already stands would release it.
    /// </summary>
    private static void Drive(in Set want, string why)
    {
        // ---- THE PERMISSIONS, read here and nowhere else -----------------------------------------
        // "die lokalen Einstellungen haben Vorrang". A denied permission does not merely skip the
        // apply: it collapses the wanted set to EMPTY, so a switch turned off while a peer's latch
        // stands RELEASES what is already showing on the very next frame instead of freezing it.
        // Local settings are a filter on the synced state; they are never negotiated over the wire,
        // and there is no bit anywhere in record 32 that could ask for them to be.
        bool sameEnvironment = want.Style == (byte)SkyAlternative.Style.Value;
        bool elementsAllowed = sameEnvironment && ElementMood.EnvironmentResponse.Value;
        bool hauntAllowed = sameEnvironment && Haunt.EasterEggs.Value
                            // …and a room to draw it in. WireStyleCode is 0 under mixed reality and
                            // outside a scenario, where Haunt.Tick does not run and the environment's
                            // stand-down clears any latch every frame — applying there would be a
                            // latch/clear loop rather than a picture. Not a permission, a surface.
                            && SkyAlternative.WireStyleCode != 0;

        // ---- elements ----------------------------------------------------------------------------
        for (int i = 0; i < Elements; i++)
        {
            bool wantStrong = elementsAllowed && (want.Strong & (1 << i)) != 0;
            bool wantWaning = elementsAllowed && (want.Waning & (1 << i)) != 0;
            bool isStrong = ElementMood.IsForced(i, waning: false);
            bool isWaning = ElementMood.IsForced(i, waning: true);
            if (wantStrong == isStrong && wantWaning == isWaning)
                continue;

            if (!wantStrong && !wantWaning)
            {
                // RELEASE is unconditional: it must work in every state a latch can survive into,
                // which is the same argument both Force implementations make for testing "pressed
                // again" before their own readiness gate.
                ElementMood.ClearForce(i, why);
            }
            else if (ElementMood.ForceReady)
            {
                // GATED ON ForceReady, and not merely to avoid wasted work: a refused Force writes a
                // log line, and this runs every frame — an ungated retry would fill the log for as
                // long as a peer held a latch while this client sat in the main menu. When the gate
                // opens (the scenario board appears) the next frame applies it, because the
                // comparison above is against the live state and not against a cache.
                ElementMood.Force(i, wantWaning);
            }
        }

        // ---- the apparition ----------------------------------------------------------------------
        int wantId = hauntAllowed && want.HauntCode > 0 ? want.HauntCode - 1 : -1;
        int liveId = Haunt.ForcedId;

        if (wantId < 0)
        {
            if (liveId >= 0)
                Haunt.ClearForce(why);
            return;
        }
        if (!Haunt.ForceReady)
            return;

        float wantSince = want.HauntSinceMillis * 0.001f;
        if (liveId != wantId)
        {
            Haunt.Force(wantId, wantSince);
            return;
        }

        // SAME CARD, DIFFERENT PRESS. The owner released and re-latched the same apparition and the
        // intervening "nothing" was lost with a packet, so the two clients would loop the same event
        // out of phase forever. Re-anchoring is a release plus a press, using the same two calls the
        // tester's own buttons use — a dedicated non-toggling setter would be a second way to write
        // the latch, and the whole reason this file drives the public API is that there is only one.
        if (Mathf.Abs(Haunt.ForcedLatchedAt - wantSince) > AnchorEpsilon)
        {
            Haunt.ClearForce("the owner re-pressed the same apparition — re-anchoring to their press "
                             + "time so both clients loop it in phase");
            Haunt.Force(wantId, wantSince);
        }
    }
}
