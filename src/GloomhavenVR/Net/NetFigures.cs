// FEATURE C — full figure-pickup sync (cosmetic, desync-safe).
//
// Figure transforms are NOT networked by the game: every client re-derives a figure's position
// from authoritative board state each frame (ActorBehaviour.Update → DoTransform / LateUpdate →
// ApplyMotion). So mirroring the figure a peer physically holds is PURELY cosmetic — we never
// send or execute an authoritative action, and on release the game's own Update snaps the mini
// straight back to its cell. This file is the send + receive + interpolation half; the transform
// suppression that lets a remotely-held figure actually track the synced pose lives in
// Board/FigureGrab/ActorBehaviour_HeldTransform_Patch (gated on NetHeldFigures.Owns).

using System.Collections.Generic;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Figure-pickup sync (cosmetic). Send side samples the figures the local player physically holds
/// (<see cref="HeldFigures"/>) as STABLE cross-client actor ids + world poses. Receive side maps
/// each id back to the local <c>ActorBehaviour</c>, adds it to <see cref="NetHeldFigures"/> so the
/// game's transform writers are suppressed for it, and eases its transform toward the pose the
/// grabber sends (like <c>RemoteAvatar</c> interpolation). Release drops it from the set and the
/// game resumes control — the mini snaps back to its board cell naturally.
///
/// <para>TWO FIGURES PER PLAYER, one per hand (hardware MP defect: "aktuell sieht man immer nur
/// eine einzige Figur maximal"). A VR player has always been able to grab a mini with each hand —
/// <see cref="HeldFigures"/> was a set from day one — but only ONE ever reached the wire. Both do
/// now, through two independent SLOTS:</para>
/// <list type="bullet">
///   <item><see cref="SlotPrimary"/> — the rig packet's held-figure block (15 Hz, unchanged since
///   the first build; that is why old peers still see the first figure).</item>
///   <item><see cref="SlotSecondary"/> — extras extension record
///   <see cref="NetProtocol.ExtIdSecondFigure"/>, on an extras packet the sender promotes to the
///   rig rate while the mini is moving, so both stream at the same cadence.</item>
/// </list>
/// <para>The slots are pinned by GRAB ORDER on the sender (<see cref="HeldFigures.TryGetSlot"/>),
/// so a figure keeps its slot for the whole hold and neither grabbing nor releasing the OTHER mini
/// disturbs its stream. The two slots are applied independently — a peer dropping one figure
/// releases exactly that one — and both are torn down together when the peer goes stale or leaves.
/// </para>
///
/// The stable id is a 32-bit FNV-1a hash of <see cref="CActor.ActorGuid"/> — the game's OWN
/// replicated per-actor GUID, stamped in the <c>CActor</c> constructor, serialized in every actor
/// state and used by the game itself as its cross-machine actor reference (e.g.
/// <c>EnemyState.ActorGuid</c>, <c>KilledByActorGuid</c> lookups over
/// <c>Scenario.AllActors</c>). Both ends of the wire derive the hash from the same replicated
/// string, so it is identical on every client for the same figure — heroes, monsters AND summons.
///
/// WHY NOT <see cref="CActor.ID"/>, which earlier builds sent (hardware MP defect: "wenn ein
/// Spieler eine BESCHWORENE Figur in die Hand nimmt, synct das falsch — man sieht eine zufällige
/// ANDERE Figur in seiner Hand"). <c>CActor.ID</c> is only unique WITHIN a class: for enemies and
/// hero summons it returns <c>StandeeID</c>, which every monster/summon class allocates from its
/// OWN 1..StandeeLimit pool (<c>CMonsterClass.ResetEnemyStandeeIDs</c> /
/// <c>CHeroSummonClass.ResetHeroSummonStandeeIDs</c>, both counting from 1). So a summon with
/// standee 1 collides with EVERY monster class's standee 1, and the receive-side id→figure lookup
/// (last write wins) routinely resolved a held summon to some unrelated monster — the "random
/// other figure in their hand". The per-actor GUID has no such per-class scoping. The 32-bit hash
/// keeps the existing wire layout byte-for-byte (rig held-figure block and extras record 8 both
/// carry a 4-byte id); a hash collision across the ≤ dozens of live figures of a scenario is
/// negligible and at worst cosmetic. Actors without a GUID (defensive — the ctor always stamps
/// one) fall back to the legacy <c>CActor.ID</c>.
///
/// Everything is a strict no-op offline / single-player (nothing is ever sampled unless a
/// figure is locally held, and no remote figure is driven unless a modded VR peer reports one).
/// </summary>
internal static class NetFigures
{
    /// <summary>Wire slot of the FIRST held figure — the rig packet's held-figure block.</summary>
    public const int SlotPrimary = 0;

    /// <summary>Wire slot of the SECOND held figure — extras record
    /// <see cref="NetProtocol.ExtIdSecondFigure"/>.</summary>
    public const int SlotSecondary = 1;

    private sealed class RemoteHeld
    {
        public ActorBehaviour Actor = null!;
        public int ActorId;
        public Vector3 TargetPos;
        public Quaternion TargetRot = Quaternion.identity;

        /// <summary>Which of the grabber's hands carries this mini, when the peer said so (only the
        /// second-figure record carries hands — the rig packet has no bit left for one). Diagnostic
        /// + seam today; the wire-level USE of the hands is the reader's "both figures cannot be in
        /// one palm" rejection, see <see cref="NetProtocol.ExtIdSecondFigure"/>.</summary>
        public bool HandKnown;
        public bool LeftHand;

        /// <summary>
        /// The figure's own LOCAL scale at its board cell, captured on the frame this hold became
        /// fresh (the same guard that spawns the home ghost, so the figure is still at home and
        /// untouched). Restored verbatim on every release path.
        /// </summary>
        public Vector3 HomeLocalScale = Vector3.one;

        /// <summary>
        /// The HOLDER's rig scale at the moment their grab was first seen here, or 0 when it was not
        /// available. DIAGNOSTIC ONLY since ModBuild 157 — it used to be the divisor of a locally
        /// reconstructed held-size ratio, and being sampled on the wrong machine at the wrong
        /// instant was one of the three causes of the reported desync (see <see cref="EaseSlot"/>).
        /// It is still captured, and still printed by the <c>[SizeSync]</c> line beside the number
        /// that replaced it, precisely so the next log can show the two side by side.
        /// </summary>
        public float GrabRigScale;

        /// <summary>
        /// The holder's HELD SIZE for this slot — the figure's rendered size as a multiple of its
        /// own board-home size — as last received on extension record 30
        /// (<see cref="NetProtocol.ExtIdHeldStretch"/>). This is the ONLY size input this client
        /// has, and deliberately so. 1 until a record arrives, reset to 1 when the record stops
        /// arriving, so an old sender — and a mini standing at board size — renders exactly as
        /// before the record existed.
        /// </summary>
        public float StretchTarget = 1f;

        /// <summary>The size factor this client is currently RENDERING, eased toward
        /// <see cref="StretchTarget"/> every <see cref="Tick"/> with the same sharpness as the
        /// pose — the record arrives at packet cadence and a raw write would step visibly
        /// ("everything moves WITH animation"). SEEDED to the target on a fresh hold whose factor
        /// is already known (<see cref="ApplyRemoteHeld"/>), so a late joiner and a re-resolved
        /// figure arrive at the right size instead of ramping to it from 1.</summary>
        public float StretchApplied = 1f;

        /// <summary>Last size factor this record actually LOGGED, and when — the throttle for the
        /// <c>[SizeSync]</c> mirror line, which would otherwise print every frame of every hold.
        /// </summary>
        public float LoggedFactor = float.NaN;
        public float LoggedAt = -999f;

        /// <summary>True once <see cref="EaseSlot"/> has written this figure's localScale at least
        /// once — the release paths restore <see cref="HomeLocalScale"/> exactly when a scale was
        /// actually touched, whatever combination of ratio and stretch touched it.</summary>
        public bool ScaleTouched;
    }

    /// <summary>Both wire slots of ONE remote player. Slots are independent: a peer holding a mini
    /// in each hand fills both, dropping one clears only that one.</summary>
    private sealed class PlayerHeld
    {
        public RemoteHeld? Primary;
        public RemoteHeld? Secondary;
    }

    // What each remote player is currently holding, keyed by playerId (two peers holding different
    // figures both work; a peer switching figures overwrites its own slot).
    private static readonly Dictionary<int, PlayerHeld> _byPlayer = new();

    /// <summary>
    /// THE LAST SIZE FACTORS EACH PEER REPORTED, kept whether or not a hold record exists for them
    /// yet — <c>[playerId] → (primary, secondary)</c>, both 1 = board size.
    ///
    /// <para>WHY IT IS SEPARATE FROM THE HOLD RECORDS. The two facts arrive on DIFFERENT PACKETS:
    /// the primary hold rides the rig packet, the size rides the extras packet, and nothing orders
    /// them. Before ModBuild 157 <see cref="ApplyHeldStretch"/> simply returned when the hold
    /// record did not exist yet, so a size that arrived first was DROPPED — and a peer whose figure
    /// was re-resolved (respawned, re-entered this client's view) started again from 1. Both read
    /// on hardware as "the peer's mini is the wrong size", which is the report. Holding the last
    /// reported value here makes the ordering irrelevant: whenever a hold record is created it is
    /// seeded from this table, and a late joiner is right on its first rendered frame rather than
    /// after a ramp from the wrong size.</para>
    ///
    /// <para>Bounded by the player count and cleared with the player (<see cref="ReleaseRemote"/>,
    /// <c>Clear</c>), so it cannot grow.</para>
    /// </summary>
    private static readonly Dictionary<int, (float Primary, float Secondary)> _lastStretch = new();

    // actorId → local ActorBehaviour cache. Rebuilt lazily on a miss or a dead cached entry from
    // the same live-figure registry FigureGrabDriver adopts from.
    private static readonly Dictionary<int, ActorBehaviour> _idLookup = new();

    private static readonly List<int> _scratchPlayers = new();
    private static readonly List<ActorBehaviour> _scratchActors = new();

    // ---- send ---------------------------------------------------------------------------

    /// <summary>
    /// Send side: the figure in wire slot <paramref name="slot"/> (<see cref="SlotPrimary"/> /
    /// <see cref="SlotSecondary"/>) — its stable id, world pose, and whether the LEFT hand holds it.
    /// False when the local player holds fewer figures than that, or the figure has no stable id.
    ///
    /// <para>The slot ordering is grab order and therefore stable for the lifetime of a hold (see
    /// <see cref="HeldFigures.TryGetSlot"/>): picking up a second mini must not move the first one
    /// out of the 15 Hz rig slot mid-stream, and dropping the second must not move the first at all.
    /// </para>
    /// </summary>
    public static bool TrySampleHeldSlot(int slot, out int actorId, out Vector3 pos,
                                         out Quaternion rot, out bool leftHand)
    {
        actorId = 0;
        pos = default;
        rot = Quaternion.identity;
        leftHand = false;

        if (!HeldFigures.TryGetSlot(slot, out ActorBehaviour actor, out HandSide side))
            return false;
        if (!TryStableId(actor, out actorId))
            return false;

        Transform? t = RootTransform(actor);
        if (t == null)
        {
            actorId = 0;
            return false;
        }

        pos = t.position;
        rot = t.rotation;
        leftHand = side == HandSide.Left;
        return true;
    }

    /// <summary>
    /// Send side: THE WHOLE HELD SIZE of the figure in wire slot <paramref name="slot"/>, as a
    /// multiple of that figure's own board-home size — measured straight off the transform the
    /// holder is looking at (<see cref="FigureGrabbable.HeldSizeFactorOf"/>). 1.0 when that slot
    /// holds nothing, when the mini is mid-glide (its size is easing home and the wire should ease
    /// the peer's copy with it), or when the mini simply stands at board size. The caller
    /// (<c>NetAvatarDriver</c>) quantizes with <see cref="NetProtocol.EncodeHeldStretch"/> and
    /// omits record 30 entirely while both slots read neutral, so a session in which nothing is
    /// resized emits the exact bytes previous builds emitted. Strict no-op offline: with nothing
    /// held it returns 1 and nothing is ever written.
    ///
    /// <para>WHY THE WHOLE SIZE AND NOT THE GESTURE FACTOR (user, 3-player hardware session
    /// 2026-08-15: "Die Größe einer Figur MUSS zwingend immer 1:1 genau die sein die der Spieler
    /// auch in der Hand hat - hier gab es oft einen Desync"). Builds through 156 sent the gesture
    /// factor alone and had <see cref="EaseSlot"/> rebuild the rest locally; the rebuild could not
    /// see the grab-time size clamp, took its zoom base from a value sampled on the WRONG machine
    /// at the WRONG instant, and pinned that base at 1 whenever the holder had no avatar yet. See
    /// <see cref="NetProtocol.ExtIdHeldStretch"/> for the three-log evidence. Sending the measured
    /// result makes the peer's size a pure function of one transmitted number.</para>
    /// </summary>
    public static float SampleHeldStretch(int slot)
    {
        if (!HeldFigures.TryGetSlot(slot, out ActorBehaviour actor, out _))
        {
            // The slot emptied. Clear the whole throttle, not just the "first line" latch: the next
            // hold is a different figure and must be logged on its own terms, never suppressed
            // because the PREVIOUS mini happened to be about this size a moment ago.
            if (slot >= 0 && slot < _ownerLoggedFactor.Length)
            {
                _ownerLoggedFactor[slot] = float.NaN;
                _ownerLoggedAt[slot] = -999f;
            }
            return 1f;
        }
        float factor = FigureGrabbable.HeldSizeFactorOf(actor);
        LogOwnerSize(slot, actor, factor);
        return factor;
    }

    // ---- the SIZE SYNC diagnostic --------------------------------------------------------
    //
    // ONE LINE PER FIGURE, ON EVERY MACHINE, WITH THE SAME NUMBERS IN THE SAME ORDER — so that
    // three logs of one session can be read side by side and the question "was the size wrong on
    // the wire, or wrong after it arrived?" is answered by grep instead of by another hardware
    // round. That question cost the 2026-08-15 round: the only held-size line those logs carry is
    // "Held-figure stretch SENT", which fires ONCE per episode and therefore records the first
    // value of a gesture and never its result — the host's log shows 1.072 for a gesture the
    // FigureGrab log shows ending at factor 6.
    //
    // SIZE SYNC OWNER prints what the holder's own transform measures and what that quantized to;
    // SIZE SYNC MIRROR prints what arrived, what is being rendered from it, and what the pre-157
    // reconstruction WOULD have said. Both name the actor by its cross-client STABLE ID, so
    // `grep "SIZE SYNC" *.log | grep <id>` lines the same figure up across three machines at the
    // same moment — the owner's line and both mirrors' lines, with every number to compare.

    /// <summary>Last OWNER factor logged per wire slot, and when — the change/time throttle. NaN
    /// means "nothing logged for this slot yet", which is how every hold is guaranteed its first
    /// line: the slot resets to NaN the moment it empties.</summary>
    private static readonly float[] _ownerLoggedFactor = { float.NaN, float.NaN };
    private static readonly float[] _ownerLoggedAt = { -999f, -999f };

    /// <summary>Seconds between repeat SIZE SYNC lines for a size that is not changing. A hold is
    /// quiet most of the time and a per-frame line would drown the log it is meant to make
    /// readable; a hold that is CHANGING passes the relative-change test below long before this.
    /// </summary>
    private const float SizeSyncQuietPeriodSeconds = 5f;

    /// <summary>Relative size change that always earns a line, whatever the quiet period says. 2%
    /// is about the smallest size difference a player can see on a mini at arm's length, so
    /// anything the user could report is in the log by construction.</summary>
    private const float SizeSyncReportableChange = 0.02f;

    private static bool DueForLine(float factor, ref float lastFactor, ref float lastAt)
    {
        float now = Time.unscaledTime;
        bool changed = float.IsNaN(lastFactor)
                       || Mathf.Abs(factor - lastFactor) > SizeSyncReportableChange * Mathf.Max(lastFactor, 1e-3f);
        if (!changed && now - lastAt < SizeSyncQuietPeriodSeconds)
            return false;
        lastFactor = factor;
        lastAt = now;
        return true;
    }

    /// <summary>
    /// OWNER half: what this machine's holder actually has in hand, what went on the wire for it,
    /// and how big the stretch gesture's interaction volume is at that size.
    /// </summary>
    private static void LogOwnerSize(int slot, ActorBehaviour actor, float factor)
    {
        if (slot < 0 || slot >= _ownerLoggedFactor.Length)
            return;
        if (!DueForLine(factor, ref _ownerLoggedFactor[slot], ref _ownerLoggedAt[slot]))
            return;

        FigureGrabbable? g = FigureGrabbable.AttachedTo(actor);
        ushort code = NetProtocol.EncodeHeldStretch(factor);
        float decoded = NetProtocol.DecodeHeldStretch(code);
        float body = g != null ? g.CaptureBodyRadiusRealMeters : float.NaN;
        float ceiling = g != null ? g.CaptureCeilingRealMeters : float.NaN;
        float reach = FigureGrabConfig.StretchReachRealMeters;
        string zone = float.IsNaN(body)
            ? "capture zone not measured this frame (no free hand to gesture with)"
            : float.IsPositiveInfinity(body)
                ? $"capture zone FELL BACK TO CENTRE — every renderer exceeded the {ceiling:0.##} m "
                  + "ceiling, which IS the 'Area nicht mitgewachsen' failure"
                : $"capture zone r={FigureStretchMath.InteractionRadiusRealMeters(body, reach):0.###} m "
                  + $"real (body {body:0.###} + reach {reach:0.###}), ceiling {ceiling:0.##} m";

        VRLog.Info("Net",
            $"SIZE SYNC OWNER slot {slot} actor {(TryStableId(actor, out int id) ? id : 0)} "
            + $"{(g != null ? g.Label : "?")}: held {factor:0.####}× board size (MEASURED lossy÷home) "
            + $"= latch {(g != null ? g.LatchTotalRatio : 1f):0.###}× × stretch "
            + $"{(g != null ? g.Stretch : 1f):0.###}× × live zoom; wire code {code} → {decoded:0.####}× "
            + $"(quantization {(decoded - factor):+0.0000;-0.0000;0.0000}); {zone}. A peer's size is "
            + "this decoded number times THEIR copy of the figure's board scale and nothing else. "
            + "So, in the peers' logs at this timestamp: a MIRROR line for this actor with a "
            + "DIFFERENT factor means the wire lost it; NO MIRROR line at all while this factor is "
            + "not 1 means the record never arrived (read the extras cadence); and the SAME factor "
            + "while the mini still looks wrong there means their board-home scale differs, which "
            + "is then the only remaining suspect.");
    }

    /// <summary>
    /// MIRROR half: what arrived for a remote hold, what is being rendered from it, and — for
    /// exactly one more round — what the pre-157 local reconstruction would have produced instead,
    /// so the replaced mechanism and its replacement can be compared inside one file.
    /// </summary>
    private static void LogMirrorSize(RemoteHeld rec, int playerId, float applied)
    {
        if (!DueForLine(applied, ref rec.LoggedFactor, ref rec.LoggedAt))
            return;

        bool rigKnown = rec.GrabRigScale > 0f
                        && NetAvatarDriver.TryGetPeerRigScale(playerId, out float rigNow)
                        && rigNow > 0f;
        string legacy = rigKnown && NetAvatarDriver.TryGetPeerRigScale(playerId, out float rigLive)
            ? $"{(rigLive / rec.GrabRigScale):0.####}×"
            : "UNKNOWN (that peer's rig scale was never available here, so it pinned the ratio at 1 "
              + "for the whole hold — one of the three causes of the reported desync)";

        VRLog.Info("Net",
            $"SIZE SYNC MIRROR player {playerId} actor {rec.ActorId}: wire {rec.StretchTarget:0.####}× "
            + $"→ rendering {applied:0.####}× × board-home {rec.HomeLocalScale.x:0.######} "
            + $"= localScale {(rec.HomeLocalScale.x * applied):0.######}. The factor is the holder's "
            + "own MEASURED size (record 30) and is the only size input used here. For comparison "
            + $"only, the pre-ModBuild-157 reconstruction would have contributed zoom ratio {legacy}, "
            + "and it was blind to the holder's grab-time size clamp entirely. If the holder's OWNER "
            + "line for this actor shows the same factor at this moment, the sync is 1:1; if it "
            + "shows a different one, read the extras-packet cadence next, not this file.");
    }

    // ---- receive ------------------------------------------------------------------------

    /// <summary>Receive side: remote <paramref name="playerId"/> holds figure
    /// <paramref name="actorId"/> in wire slot <paramref name="slot"/> at the given world pose. Maps
    /// the id to a local actor, records the target pose, and suppresses the game's writes for it. If
    /// the id can't be resolved locally (figure not spawned in this view) only THAT slot is cleared,
    /// so a peer's other hand keeps its mini.
    ///
    /// <para><paramref name="handKnown"/>/<paramref name="leftHand"/> come from the second-figure
    /// record, the only place hands ride the wire; the primary slot's hand is stamped by the same
    /// record while both hands are full and left unknown otherwise.</para></summary>
    public static void ApplyRemoteHeld(int playerId, int slot, int actorId, Vector3 pos,
                                       Quaternion rot, bool handKnown = false, bool leftHand = false)
    {
        ActorBehaviour? actor = Resolve(actorId);
        if (actor == null)
        {
            ReleaseRemoteSlot(playerId, slot);
            return;
        }

        // TASK #3 (multiplayer ghost): the FIRST frame this figure becomes remotely held — before it
        // is added to NetHeldFigures / eased toward the remote hand below — it is still sitting at its
        // authoritative board cell. Capture that HOME pose now and spawn the translucent ghost so it
        // appears at the home spot on THIS client for the whole time the peer holds it. Guarded to a
        // genuinely fresh hold (not already held by any peer or locally) so it is captured at home;
        // NotifyHeld is idempotent regardless. Despawn is reconciled by FigureGhosts.Tick when the
        // release drops the actor from NetHeldFigures (RebuildSet, ReleaseRemote, or the Tick prune).
        if (!NetHeldFigures.Owns(actor) && !HeldFigures.Owns(actor))
        {
            // ONE resolver with the local path (ModBuild 335) — the clone and the pose must never
            // come from different objects, and m_AnimatedGameObject is not reliably the figure.
            GameObject? animated = FigureGhosts.GhostSource(actor);
            if (animated != null)
                FigureGhosts.NotifyHeld(actor, animated.transform.position, animated.transform.rotation);
        }

        if (!_byPlayer.TryGetValue(playerId, out PlayerHeld player))
        {
            player = new PlayerHeld();
            _byPlayer[playerId] = player;
        }

        RemoteHeld? rec = slot == SlotSecondary ? player.Secondary : player.Primary;

        // A slot whose ACTOR CHANGED is a new hold, not a continuing one: the peer swapped minis, or
        // the game respawned this one and Resolve handed back a different ActorBehaviour. The
        // outgoing figure must get its board scale back (nothing else ever restores it — see
        // RestoreHomeScale) and the incoming one must have its OWN home size captured, or it
        // inherits the previous figure's and renders at the wrong size for the rest of the hold.
        if (rec != null && !ReferenceEquals(rec.Actor, actor))
        {
            RestoreHomeScale(rec);
            rec = null;
            if (slot == SlotSecondary) player.Secondary = null;
            else player.Primary = null;
        }

        if (rec == null)
        {
            rec = new RemoteHeld();
            if (slot == SlotSecondary) player.Secondary = rec;
            else player.Primary = rec;

            // SEED THE SIZE FROM WHAT THIS PEER LAST REPORTED, rather than starting at 1 and easing
            // up. The size and the hold arrive on different packets in no fixed order, so a fresh
            // record routinely knows the factor already — a late joiner, a re-resolved figure, a
            // hold whose extras packet simply landed first. Starting at board size would show the
            // mini at the wrong size for the length of the ease, every time.
            if (_lastStretch.TryGetValue(playerId, out (float Primary, float Secondary) last))
            {
                rec.StretchTarget = slot == SlotSecondary ? last.Secondary : last.Primary;
                rec.StretchApplied = rec.StretchTarget;
            }

            // THE FIGURE'S BOARD-HOME SIZE, CAPTURED ONCE PER HOLD (2026-08-11). A fresh record is
            // the receive-side equivalent of the grab edge, and the figure has not been eased
            // anywhere yet, so its transform still carries the board-cell scale. This is the ONE
            // local number EaseSlot multiplies the wire factor by, and it is authoritative game
            // state — the same cell on every machine — which is exactly why it is allowed to be
            // local at all. Re-reading it per frame would read back the size WE just wrote.
            Transform? home = RootTransform(actor);
            if (home != null)
                rec.HomeLocalScale = home.localScale;
            // Diagnostic only since ModBuild 157 (see RemoteHeld.GrabRigScale): the [SizeSync] line
            // prints the ratio this value WOULD have produced, next to the factor that arrived, so
            // one log can show whether the old reconstruction and the new number agree.
            rec.GrabRigScale = NetAvatarDriver.TryGetPeerRigScale(playerId, out float rigNow) ? rigNow : 0f;
        }
        rec.Actor = actor;
        rec.ActorId = actorId;
        rec.TargetPos = pos;
        rec.TargetRot = rot;
        rec.HandKnown = handKnown;
        rec.LeftHand = leftHand;

        DropDuplicateSecondary(player);
        RebuildSet();
    }

    /// <summary>Receive side: stamp the hand carried for the peer's PRIMARY figure. Only the
    /// second-figure record knows it (the rig flag byte has no bit left), so it arrives on the
    /// extras packet, one channel over from the figure it describes.</summary>
    public static void NotePrimaryHand(int playerId, bool leftHand)
    {
        if (_byPlayer.TryGetValue(playerId, out PlayerHeld player) && player.Primary != null)
        {
            player.Primary.HandKnown = true;
            player.Primary.LeftHand = leftHand;
        }
    }

    /// <summary>
    /// Receive side: <paramref name="playerId"/>'s HELD SIZE factors (extension record 30),
    /// slot-aligned with the two held-figure slots — each the figure's rendered size as a multiple
    /// of its own board-home size, exactly as the holder measured it. Stored as TARGETS —
    /// <see cref="EaseSlot"/> eases the rendered factor toward them, so the packet-cadence steps
    /// arrive as motion.
    ///
    /// <para>THE VALUE IS KEPT EVEN WHEN NO HOLD RECORD EXISTS YET (<see cref="_lastStretch"/>).
    /// The size and the hold ride different packets in no fixed order; through ModBuild 156 this
    /// method returned early in that case and the size was simply lost, which is one of the three
    /// ways a peer could end up showing a mini at a size its holder never had. Nothing is skipped
    /// now: the table is written first, and a hold record created later seeds itself from it.</para>
    /// </summary>
    public static void ApplyHeldStretch(int playerId, float primary, float secondary)
    {
        _lastStretch[playerId] = (primary, secondary);
        if (!_byPlayer.TryGetValue(playerId, out PlayerHeld player))
            return;
        if (player.Primary != null)
            player.Primary.StretchTarget = primary;
        if (player.Secondary != null)
            player.Secondary.StretchTarget = secondary;
    }

    /// <summary>
    /// Receive side: an extras packet from <paramref name="playerId"/> WITHOUT record 30 —
    /// absence means both factors are 1.0, i.e. both minis are held at exactly board size (the
    /// record's contract, and what an old sender always transmits). Eased back like any other
    /// change, never snapped. Clears the pending table too, so a hold created after this packet
    /// seeds itself to board size rather than to a stale factor.
    /// </summary>
    public static void ResetHeldStretch(int playerId)
    {
        _lastStretch.Remove(playerId);
        if (!_byPlayer.TryGetValue(playerId, out PlayerHeld player))
            return;
        if (player.Primary != null)
            player.Primary.StretchTarget = 1f;
        if (player.Secondary != null)
            player.Secondary.StretchTarget = 1f;
    }

    /// <summary>Receive side: <paramref name="playerId"/> released the figure in one wire slot. That
    /// figure leaves <see cref="NetHeldFigures"/> and the game's Update snaps it back to its cell;
    /// anything in the OTHER slot keeps riding that peer's other hand.</summary>
    public static void ReleaseRemoteSlot(int playerId, int slot)
    {
        if (!_byPlayer.TryGetValue(playerId, out PlayerHeld player))
            return;

        if (slot == SlotSecondary)
        {
            if (player.Secondary == null)
                return;
            RestoreHomeScale(player.Secondary); // scale is the one thing the game will not re-author
            player.Secondary = null;
        }
        else
        {
            if (player.Primary == null)
                return;
            RestoreHomeScale(player.Primary);
            player.Primary = null;
        }

        if (player.Primary == null && player.Secondary == null)
            _byPlayer.Remove(playerId);
        RebuildSet();
    }

    /// <summary>Receive side: <paramref name="playerId"/> released EVERYTHING (peer left, went
    /// stale, switched to flat, session torn down). Both slots go, so no ghost figure is ever left
    /// hanging in a hand nobody is attached to any more.</summary>
    public static void ReleaseRemote(int playerId)
    {
        if (_byPlayer.TryGetValue(playerId, out PlayerHeld leaving))
        {
            // Both slots, before the record goes: a peer who leaves mid-hold must not leave a mini
            // standing on the board at their zoom (see RestoreHomeScale).
            RestoreHomeScale(leaving.Primary);
            RestoreHomeScale(leaving.Secondary);
        }
        _lastStretch.Remove(playerId); // a departed peer's last reported size must not seed a re-join
        if (_byPlayer.Remove(playerId))
            RebuildSet();
    }

    // ---- interpolation ------------------------------------------------------------------

    /// <summary>Per-frame ease of every remotely-held figure toward its target pose (exponential
    /// Lerp/Slerp, same sharpness as RemoteAvatar). Both slots of every peer are eased IDENTICALLY —
    /// that, plus the sender streaming both at the same rate, is what makes the two minis in a
    /// peer's hands move alike. Prunes a slot whose actor was destroyed. No-op when no remote figure
    /// is held.</summary>
    public static void Tick()
    {
        if (_byPlayer.Count == 0)
            return;

        float dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
        float k = 1f - Mathf.Exp(-NetProtocol.InterpolationSharpness * dt);

        _scratchPlayers.Clear();
        bool pruned = false;
        foreach (KeyValuePair<int, PlayerHeld> kv in _byPlayer)
        {
            PlayerHeld player = kv.Value;
            if (!EaseSlot(player.Primary, kv.Key, k)) { RestoreHomeScale(player.Primary); player.Primary = null; pruned = true; }
            if (!EaseSlot(player.Secondary, kv.Key, k)) { RestoreHomeScale(player.Secondary); player.Secondary = null; pruned = true; }
            if (player.Primary == null && player.Secondary == null)
                _scratchPlayers.Add(kv.Key);
        }

        for (int i = 0; i < _scratchPlayers.Count; i++)
            _byPlayer.Remove(_scratchPlayers[i]);
        if (pruned)
            RebuildSet();
    }

    /// <summary>Ease one slot toward its target. False when the slot is gone (empty, or its actor
    /// was destroyed) and must be dropped.</summary>
    private static bool EaseSlot(RemoteHeld? rec, int playerId, float k)
    {
        if (rec == null)
            return false;
        Transform? t = RootTransform(rec.Actor);
        if (t == null)
            return false; // actor gone → drop this hold
        t.position = Vector3.Lerp(t.position, rec.TargetPos, k);
        t.rotation = Quaternion.Slerp(t.rotation, rec.TargetRot, k);

        // HELD SIZE — ONE TRANSMITTED NUMBER, NOTHING RE-DERIVED (ModBuild 157). The size a peer
        // shows is this client's own copy of the figure's board-home scale times the factor the
        // HOLDER measured off their own transform and sent on record 30. That is the whole
        // calculation, and it is deliberately the whole calculation: the user's requirement is
        // absolute ("Die Größe einer Figur MUSS zwingend immer 1:1 genau die sein die der Spieler
        // auch in der Hand hat"), and the only way to keep it is for the mirror to have no opinion.
        // HomeLocalScale is the single local input, and it is authoritative game state — the same
        // board cell on every machine — not a reconstruction.
        //
        // WHAT THIS REPLACED, and why it had to go. Builds through 156 computed
        // homeLocalScale × (peerRigNow ÷ peerRigAtHoldStart) × gestureFactor. Every term but the
        // last was local guesswork about the holder's machine, and it was wrong three ways at once:
        // the holder's GRAB-TIME SIZE CLAMP was invisible to it (three logs of the 2026-08-15
        // session carry that clamp, worst case a 10× grab trimmed to 3× ⇒ this mirror 3.33× too
        // large for the whole hold); peerRigAtHoldStart was sampled HERE, from a value that arrived
        // up to a packet late, on the frame the hold record happened to reach us; and it degraded
        // to 1 — silently, permanently for that hold — whenever the holder had no avatar yet.
        //
        // The factor is still EASED with the same k as the pose, because it arrives at packet
        // cadence and a raw write would step visibly. Neutral (1.0) unless a record said otherwise,
        // so an old sender and a mini standing at board size both render exactly the previous
        // picture.
        rec.StretchApplied = Mathf.Lerp(rec.StretchApplied, rec.StretchTarget, k);
        float sizeFactor = rec.StretchApplied;
        // Write only when something actually asks for a non-home size (or asked before and is
        // easing back): a hold whose factor is neutral must not start authoring scale at all — that
        // is the degrade-to-old-behaviour contract above.
        if (rec.ScaleTouched || !Mathf.Approximately(sizeFactor, 1f))
        {
            Vector3 want = rec.HomeLocalScale * sizeFactor;
            if (t.localScale != want)
                t.localScale = want;
            rec.ScaleTouched = true;
            LogMirrorSize(rec, playerId, sizeFactor);
            // FIGURE RESCALE, the peer half (user, ModBuild 137: "Beim Skallieren der Figuren
            // skallieren nicht alle Assets richtig mit … Alle Teile der Figur sollen korrekt
            // mitskallieren"). A cape is a UnityEngine.Cloth whose per-vertex constraints are
            // absolute distances seeded once at spawn (ActorBehaviour.cs:122) and never re-seeded by
            // the game, so it does not follow a transform scale. The MIRROR must apply exactly the
            // same correction as the holder's own machine or the two clients show different capes at
            // the same size — the 1:1 ruling. Zero wire bytes of its own: the factor is the one that
            // arrived on record 30, i.e. the same number the holder's FigureGrabbable measured, so
            // both sides feed the cloth solver identical input by construction (through 156 this was
            // a local reconstruction and inherited every error of one).
            // See Board/FigureGrab/FigureCloth.
            FigureCloth.Note(t.gameObject, sizeFactor);
        }
        return true;
    }

    // ---- helpers ------------------------------------------------------------------------

    /// <summary>
    /// Drop a SECONDARY slot that names the same actor as the PRIMARY one.
    ///
    /// <para>This is the transition guard for "the peer released their FIRST figure while still
    /// holding the second". The sender re-packs its slots by grab order, so the surviving mini moves
    /// into the primary slot on the very next rig packet — while the last extras packet, sent up to
    /// one extras interval earlier, still names it as the second figure. Without this, one mini
    /// would briefly be driven by TWO records whose poses are a packet apart, and it would visibly
    /// buzz between them. Deduplicating on identity costs one reference compare and removes the
    /// whole class.</para>
    /// </summary>
    private static void DropDuplicateSecondary(PlayerHeld player)
    {
        if (player.Primary != null && player.Secondary != null
            && ReferenceEquals(player.Primary.Actor, player.Secondary.Actor))
            player.Secondary = null;
    }

    /// <summary>Rebuild <see cref="NetHeldFigures"/> as the union of every player's held actors —
    /// keeps suppression (and therefore the local grab lock) correct when peers hold different
    /// figures, when one peer holds TWO, or when two peers name the same one.</summary>
    private static void RebuildSet()
    {
        _scratchActors.Clear();
        foreach (PlayerHeld player in _byPlayer.Values)
        {
            if (player.Primary != null && player.Primary.Actor != null)
                _scratchActors.Add(player.Primary.Actor);
            if (player.Secondary != null && player.Secondary.Actor != null)
                _scratchActors.Add(player.Secondary.Actor);
        }
        NetHeldFigures.ReplaceWith(_scratchActors);
    }

    /// <summary>
    /// The cross-client-stable actor id: FNV-1a(32) of the game's replicated
    /// <see cref="CActor.ActorGuid"/> (see the class doc for why the old per-class
    /// <see cref="CActor.ID"/> mis-resolved SUMMONS on the receiving machine). Send and receive
    /// sides both funnel through this one function, so the derivation can never diverge between
    /// the sampler and the lookup. False when the actor is gone; a guid-less actor (defensive)
    /// degrades to the legacy id.
    /// </summary>
    private static bool TryStableId(ActorBehaviour actor, out int id)
    {
        id = 0;
        CActor? ca = actor != null ? actor.Actor : null;
        if (ca == null)
            return false;
        id = StableActorId(ca);
        return id != 0;
    }

    /// <summary>
    /// The cross-client-stable id of a <see cref="CActor"/> (0 = unknowable) — the ONE id space
    /// every actor-referencing wire record rides: the held-figure records (8 + the rig block)
    /// AND the initiative-track hover (record 16). Shared on purpose: any record that named an
    /// actor by the per-class <see cref="CActor.ID"/> instead would inherit the summon-collision
    /// ambiguity this hash exists to kill (see the class doc).
    /// </summary>
    internal static int StableActorId(CActor? ca)
    {
        if (ca == null)
            return 0;
        string? guid = null;
        try { guid = ca.ActorGuid; } catch { /* mid-teardown actor — fall through */ }
        if (!string.IsNullOrEmpty(guid))
            return Fnv1a32(guid!);
        try
        {
            return ca.ID;
        }
        catch
        {
            return 0; // CActor.ID throws for an unsupported actor type
        }
    }

    /// <summary>
    /// FNV-1a, 32-bit, over the guid's UTF-16 code units — deterministic across machines and
    /// runs (no <c>string.GetHashCode</c>, whose value is process-randomizable on modern
    /// runtimes and was never contractual on Mono either). Zero is remapped so an id of 0 can
    /// keep meaning "no figure" everywhere the wire defaults it.
    /// </summary>
    private static int Fnv1a32(string s)
    {
        unchecked
        {
            uint hash = 2166136261u;
            for (int i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash *= 16777619u;
            }
            int id = (int)hash;
            return id != 0 ? id : 1;
        }
    }

    /// <summary>
    /// Put a released remote figure back at its board-cell scale.
    ///
    /// <para>THIS MUST BE CALLED ON EVERY RELEASE PATH, and it is the one thing about the held-size
    /// mirror that does NOT heal itself. Position and rotation are re-authored by the game's own
    /// Update the moment a figure leaves <see cref="NetHeldFigures"/>, which is why the release
    /// paths never had to restore those — but the game never writes a figure's SCALE, so a mini
    /// released while its holder was zoomed in would keep the enlarged size forever.</para>
    ///
    /// <para>Writes only when it differs, and only when the hold actually AUTHORED a scale
    /// (<see cref="RemoteHeld.ScaleTouched"/> — set by <see cref="EaseSlot"/> for the zoom ratio
    /// and the manual stretch alike), so a record that never touched the figure's size (ratio
    /// unavailable, stretch neutral, or an actor that is gone) is a no-op.</para>
    /// </summary>
    private static void RestoreHomeScale(RemoteHeld? rec)
    {
        if (rec == null || !rec.ScaleTouched)
            return;
        Transform? t = RootTransform(rec.Actor);
        if (t == null)
            return;
        if (t.localScale != rec.HomeLocalScale)
            t.localScale = rec.HomeLocalScale;
        // Back at board size — tell FigureCloth so the cape's authored coefficients are restored
        // verbatim and its simulation ramped back to full. Unconditional (not gated on the write
        // above) because the size may already be home while the cloth is still mid-correction.
        FigureCloth.Note(t.gameObject, 1f);
    }

    private static Transform? RootTransform(ActorBehaviour actor)
    {
        if (actor == null)
            return null;
        GameObject root = actor.m_RootGameObject;
        return root != null ? root.transform : null;
    }

    /// <summary>Map a stable actor id to the local <c>ActorBehaviour</c>, rebuilding the cache on a
    /// miss or a dead entry. Null when no live figure matches (different scenario view, not spawned
    /// yet).</summary>
    private static ActorBehaviour? Resolve(int actorId)
    {
        if (_idLookup.TryGetValue(actorId, out ActorBehaviour cached) && cached != null && cached.Actor != null)
            return cached;

        RebuildLookup();
        return _idLookup.TryGetValue(actorId, out ActorBehaviour found) && found != null ? found : null;
    }

    private static void RebuildLookup()
    {
        _idLookup.Clear();

        // Same live-figure registry FigureGrabDriver adopts grabbables from, so the ids line up
        // with what the local player can grab. Fallback to a scene scan only if it's unavailable.
        WorldspaceUITools tools = WorldspaceUITools.Instance;
        if (tools != null && tools._panelUIControllers != null)
        {
            List<WorldspacePanelUIController> controllers = tools._panelUIControllers;
            for (int i = 0; i < controllers.Count; i++)
            {
                WorldspacePanelUIController controller = controllers[i];
                if (controller == null)
                    continue;
                GameObject figure = controller.m_ObjectToTrack;
                if (figure == null)
                    continue;
                Index(ActorBehaviour.GetActorBehaviour(figure));
            }
        }
        else
        {
            foreach (ActorBehaviour ab in Object.FindObjectsOfType<ActorBehaviour>())
                Index(ab);
        }
    }

    private static void Index(ActorBehaviour? ab)
    {
        // Ids are GUID hashes now, so a collision is a genuine 2^-32 accident rather than the
        // systematic per-class StandeeID overlap that used to make "last write wins" resolve a
        // held summon to a random other figure. Still last-write-wins: cosmetic only.
        if (ab != null && TryStableId(ab, out int id))
            _idLookup[id] = ab;
    }
}
