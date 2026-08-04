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
            GameObject? animated = actor.m_AnimatedGameObject != null
                ? actor.m_AnimatedGameObject
                : actor.m_RootGameObject;
            if (animated != null)
                FigureGhosts.NotifyHeld(actor, animated.transform.position, animated.transform.rotation);
        }

        if (!_byPlayer.TryGetValue(playerId, out PlayerHeld player))
        {
            player = new PlayerHeld();
            _byPlayer[playerId] = player;
        }

        RemoteHeld? rec = slot == SlotSecondary ? player.Secondary : player.Primary;
        if (rec == null)
        {
            rec = new RemoteHeld();
            if (slot == SlotSecondary) player.Secondary = rec;
            else player.Primary = rec;
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
            player.Secondary = null;
        }
        else
        {
            if (player.Primary == null)
                return;
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
            if (!EaseSlot(player.Primary, k)) { player.Primary = null; pruned = true; }
            if (!EaseSlot(player.Secondary, k)) { player.Secondary = null; pruned = true; }
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
    private static bool EaseSlot(RemoteHeld? rec, float k)
    {
        if (rec == null)
            return false;
        Transform? t = RootTransform(rec.Actor);
        if (t == null)
            return false; // actor gone → drop this hold
        t.position = Vector3.Lerp(t.position, rec.TargetPos, k);
        t.rotation = Quaternion.Slerp(t.rotation, rec.TargetRot, k);
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
