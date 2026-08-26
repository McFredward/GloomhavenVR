using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// WHICH CHARACTER a peer's mirrored board must render — the one question every per-actor remote
/// surface used to answer with "the character the game says that peer owns", and which the FREE
/// CHARACTER FOCUS feature (ModBuild 80-83) made wrong.
///
/// User, verbatim: "Jegliche Anzeige eines Boards soll 1:1 synchronisiert werden im Remote-Board,
/// auch wenn der Remote-Spieler einen anderen Character ausgewählt hat." A peer who is LOOKING at
/// another character sees THAT character's hand, slots, piles and readouts on their own board; a
/// mirror that keeps drawing their owned character is not a mirror, it is a second, divergent
/// board that happens to sit at their pose.
///
/// ─── WHERE THE ANSWER COMES FROM ───────────────────────────────────────────────────────────────
/// EXTENSION RECORD 22 (<see cref="NetProtocol.ExtIdCharFocus"/>) already carries it: the stable
/// <c>NetFigures.StableActorId</c> of the character that peer is effectively looking at
/// (<c>Board.CharacterFocus.LookingAt</c> on their machine). The receiver keeps it in
/// <c>Board.CharacterFocus.FocusIdForPeer(playerId)</c>. NOTHING NEW GOES ON THE WIRE for this —
/// which is the whole point: the id is a hash of a REPLICATED <c>CActor.ActorGuid</c>, so
/// resolving it back to a live <c>CPlayerActor</c> is a purely LOCAL lookup against this client's
/// own replicated scenario, and no card identity, no hand content and no per-character secret ever
/// rides our channel.
///
/// ─── THE THREE RULES ───────────────────────────────────────────────────────────────────────────
///  1. SECRECY WINS OVER FIDELITY. While the game holds its one real secret — the ability-card
///     selection window (<see cref="RevealGate.IsSecretSelectionPhase"/>) — the focus is IGNORED
///     and every board falls back to its owner's owned character. That is not a compromise: the
///     SENDER refuses to change focus in that window at all
///     (<c>Board.CharacterFocus.Refusal</c> mints exactly this one refusal), so a focus id that
///     survives into the secret phase is stale by construction. Following it would only widen the
///     set of characters whose cards a viewer's own <see cref="RevealGate"/> has to arbitrate,
///     for zero fidelity gain.
///  2. THE VIEWER'S GATE STILL DECIDES WHAT IS DRAWN. This class picks WHICH character a surface
///     is about; it never says anything may be SHOWN. Every caller keeps passing the resolved
///     actor through <see cref="RevealGate.ShowRoundCardFronts"/> exactly as before, so the
///     mirror can never show more than this client is itself allowed to see — see
///     <see cref="RevealGate.ShowBattleGoal"/> for the per-character surfaces where "what the
///     owner sees" and "what the viewer may see" genuinely differ.
///  3. FALL BACK, NEVER BLANK. An unresolvable focus id (the character is not in THIS client's
///     scenario yet, it died, the peer focused a summon) degrades to the owned character — i.e.
///     to the exact behaviour every build before this one had. A remote board is never left
///     without an actor because of this feature.
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire of its own. It re-uses extension record 22
/// (already spent on the focus rings) as a SELECTOR into the host-replicated model, and resolves it
/// through <c>ScenarioManager.Scenario.AllPlayers</c> on the receiving client. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal static class RemoteBoardFocus
{
    /// <summary>Minimum seconds between two full <c>AllPlayers</c> walks. The walk allocates (the
    /// game's property is <c>PlayerActors.Concat(ExhaustedPlayers).ToList()</c>), and a focus id
    /// this client cannot resolve at all — a summon, a character not spawned here yet — would
    /// otherwise re-walk once per board per frame forever.</summary>
    private const float RebuildThrottleSeconds = 0.5f;

    /// <summary>Stable actor id → live player actor, rebuilt lazily. Entries are re-verified on
    /// every read (the id is derived from the actor's own guid), so a stale entry cannot survive a
    /// hit.</summary>
    private static readonly Dictionary<int, CPlayerActor> _byId = new();

    private static float _nextRebuildAt;

    /// <summary>The scenario the index was built against. A different instance (new scenario, a
    /// reload) clears the index by itself, so this class needs NO teardown hook in the module
    /// lifecycle and can never keep a dead scenario's actors alive.</summary>
    private static CScenario? _indexedScenario;

    /// <summary>Per-peer change gate for the diagnostic line (playerId → last logged actor id).</summary>
    private static readonly Dictionary<int, int> _logged = new();

    /// <summary>
    /// The live <see cref="CPlayerActor"/> behind a stable actor id, or null. Walks the game's own
    /// <c>ScenarioManager.Scenario.AllPlayers</c> (players + exhausted players — the exact domain
    /// <c>Board.CharacterFocus.IsFocusTarget</c> draws its targets from) and matches on the SAME
    /// hash the sender used, so the two ends can never disagree about the id space.
    ///
    /// <para>Deliberately NOT <c>NetFigures</c>' figure lookup: that one resolves an id to a live
    /// <c>ActorBehaviour</c> (a scene FIGURE, for the held-figure records), which is a strictly
    /// narrower set — a character whose standee is mid-respawn has no behaviour but is very much
    /// still the character whose board content we must draw.</para>
    /// </summary>
    internal static CPlayerActor? ActorById(int actorId)
    {
        if (actorId == 0)
            return null;

        // A scenario swap invalidates everything at once — and drops our last references to the
        // old scenario's actors, so the index can never pin a torn-down graph in memory.
        CScenario? live = LiveScenario();
        if (!ReferenceEquals(live, _indexedScenario))
        {
            _byId.Clear();
            _indexedScenario = live;
            _nextRebuildAt = 0f;
        }
        if (live == null)
            return null;

        if (_byId.TryGetValue(actorId, out CPlayerActor cached)
            && cached != null && NetFigures.StableActorId(cached) == actorId)
            return cached;

        float now = Time.unscaledTime;
        if (now < _nextRebuildAt)
            return null;
        _nextRebuildAt = now + RebuildThrottleSeconds;
        Rebuild();
        return _byId.TryGetValue(actorId, out CPlayerActor found) && found != null ? found : null;
    }

    /// <summary>Re-index every player character of the running scenario. Guarded whole: a
    /// half-torn scenario must degrade to an empty index (⇒ every board falls back to its owner's
    /// character), never throw inside a remote-avatar tick.</summary>
    private static CScenario? LiveScenario()
    {
        try { return ScenarioManager.Scenario; }
        catch { return null; }
    }

    private static void Rebuild()
    {
        _byId.Clear();
        try
        {
            CScenario? scenario = _indexedScenario;
            List<CPlayerActor>? players = scenario != null ? scenario.AllPlayers : null;
            if (players == null)
                return;
            for (int i = 0; i < players.Count; i++)
            {
                CPlayerActor p = players[i];
                if (p == null)
                    continue;
                int id = NetFigures.StableActorId(p);
                if (id != 0)
                    _byId[id] = p;
            }
        }
        catch (System.Exception e)
        {
            _byId.Clear();
            VRLog.Debug("Net", $"Remote board focus: player index rebuild failed ({e.Message}) — " +
                               "every remote board falls back to its owner's own character this tick.");
        }
    }

    /// <summary>
    /// THE ONE RESOLUTION every per-actor remote surface funnels through: the character
    /// <paramref name="owner"/>'s mirrored board must be ABOUT this frame.
    ///
    /// Returns their FOCUSED character when record 22 names one this client can resolve and the
    /// secret selection window is not open; otherwise the character the game says they own
    /// (<c>NetPlayerActors.ActorFor</c>) — which is what every build before ModBuild 84 always
    /// returned. <paramref name="viaFocus"/> reports which of the two happened, purely for the
    /// diagnostics and for callers that want to say so in a log line.
    /// </summary>
    internal static CPlayerActor? DisplayedActor(RemoteAvatar owner, out bool viaFocus)
    {
        viaFocus = false;
        if (owner == null)
            return null;

        CPlayerActor? owned = NetPlayerActors.ActorFor(owner.PlayerId);

        // RULE 1 — secrecy wins. See the class doc: the sender cannot legally change focus inside
        // this window, so anything we still hold for them is stale, and following it would only
        // move a card question onto a character the viewer has to re-arbitrate.
        if (RevealGate.IsSecretSelectionPhase)
        {
            Log(owner.PlayerId, owned, viaFocus: false, secretPhase: true);
            return owned;
        }

        int focusId = Board.CharacterFocus.FocusIdForPeer(owner.PlayerId);
        if (focusId == 0)
        {
            Log(owner.PlayerId, owned, viaFocus: false, secretPhase: false);
            return owned;
        }

        CPlayerActor? focused = ActorById(focusId);
        // A dead / exhausted character is not a focus TARGET on the sender either
        // (Board.CharacterFocus.IsFocusTarget), so treating it as unresolvable here keeps the two
        // ends agreeing instead of drawing a board about a character its owner already left.
        if (focused == null || IsDead(focused))
        {
            Log(owner.PlayerId, owned, viaFocus: false, secretPhase: false);
            return owned;
        }

        viaFocus = !ReferenceEquals(focused, owned);
        Log(owner.PlayerId, focused, viaFocus, secretPhase: false);
        return focused;
    }

    /// <summary>Guarded <c>IsDead</c> — a mid-teardown actor reads as gone rather than throwing
    /// inside the board tick.</summary>
    private static bool IsDead(CPlayerActor actor)
    {
        try { return actor.IsDead; }
        catch { return true; }
    }

    /// <summary>
    /// One Info line per PEER per actual change of the character their board is about (grep:
    /// "Remote board character"). This is the anchor a "sein Brett zeigt den falschen Character"
    /// report is decided from: it states the character, whether the focus record or the ownership
    /// bridge produced it, and — when the secret window is what suppressed the focus — says so, so
    /// the log never reads like the record went missing.
    /// </summary>
    private static void Log(int playerId, CPlayerActor? actor, bool viaFocus, bool secretPhase)
    {
        int key = actor != null ? NetFigures.StableActorId(actor) : 0;
        if (viaFocus)
            key = -key; // the SOURCE is part of the state a change gate must react to
        if (_logged.TryGetValue(playerId, out int last) && last == key)
            return;
        _logged[playerId] = key;

        string who = Board.CharacterFocus.Describe(actor);
        VRLog.Info("Net", $"Remote board character [{playerId}] = {who} — " +
                          (viaFocus
                              ? "the character this peer is LOOKING at (extension record 22, " +
                                "resolved locally against this client's own replicated scenario). " +
                                "Their mirrored hand, slots, piles and readouts follow their focus, " +
                                "not their ownership."
                              : secretPhase
                                  ? "their OWNED character: the game is in the secret " +
                                    "card-selection phase, where the focus feature refuses to " +
                                    "switch at all, so a peer's focus record is ignored by design."
                                  : "their OWNED character (no usable focus record — a pre-record " +
                                    "build, no scenario, or a focus this client cannot resolve)."));
    }
}
