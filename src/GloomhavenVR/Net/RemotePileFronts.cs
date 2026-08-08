using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// THE FRONT LAYER OF A PEER'S PILE FANS — the shared driver that turns the back-only card slabs of
/// <see cref="RemoteBrowserFan"/> (their discard / burnt / items browse arc) and
/// <see cref="RemoteItemFan"/> (their equipped-item arc) into readable card faces, and back again.
///
/// ─── THE RULING THIS IMPLEMENTS (user, 2026-08-08) ─────────────────────────────────────────────
/// "Die Oberseiten der Karten des remote Spielers soll auch überall sichtbar sein, sei es Karten in
/// der Hand, der Hand-Karten-Pile oder einer der Piles aus dem Board (Items/Abgeworfen/Verbrannt).
/// Also das soll für alle Karten und piles gelten. NUR in der Auswahlphase sieht man überall nur die
/// Rückseiten von remote spielern, in allen anderen Phasen, ist alles sichtbar."
///
/// The secrecy rule is therefore a PHASE, not a PLACE. Before this class, the phase rule governed only
/// the two round-card slots and the hand fan; every pile fan was card BACKS unconditionally, which is
/// a PLACE rule, and the two disagreed the moment the selection phase ended. One predicate now decides
/// every one of them: <see cref="RevealGate.ShowRoundCardFronts"/>.
///
/// ─── ZERO WIRE, AND WHY THAT IS NOT IN TENSION WITH THE WIRE RULE ──────────────────────────────
/// "Never card identity on the wire" is a WIRE rule and it is untouched: the fans' wire payload is
/// still a pile KIND, a COUNT and a placement. The identities come from the peer's OWN host-replicated
/// model, which this client already holds in full:
///   * DISCARD / BURNT — <c>CCharacterClass.Discarded/Lost/PermanentlyLostAbilityCards</c>, resolved to
///     the live per-actor <c>AbilityCardUI</c> widgets through <c>CardsGameApi.GetPileWidgets</c>. That
///     is the SAME call, in the SAME order, that the owner's own <c>PileBrowser</c> is filled from, so
///     the mirrored arc is card-for-card and slot-for-slot what they are looking at.
///   * ITEMS — <c>CPlayerActor.Inventory.AllItems</c>, the unfiltered list <c>Cards.ItemsPile</c> reads
///     for the local fan, faces manufactured by <see cref="RemoteItemCardSource"/>.
/// Nothing is written; every read is null-guarded; every failure path ends in card BACKS, which is the
/// safe direction by construction.
///
/// ─── COST ──────────────────────────────────────────────────────────────────────────────────────
/// A peer's fan can be a dozen cards and there can be three peers, so nothing expensive may sit on the
/// per-frame path. Two gates keep it off:
///   1. RESOLUTION runs on the board-content cadence (<see cref="RemoteBoardContent.RefreshSeconds"/>,
///      4 Hz by default), plus immediately on a real edge (slab count, pile kind, or the reveal gate
///      flipping). In between, the per-frame call is a clock compare and a walk of
///      <see cref="RemoteCardArt.MaintainMipBake"/>, which itself early-returns on its own 1 s cadence.
///   2. CLONING is change-gated inside <see cref="RemoteCardArt"/> on a per-slot key — the live
///      widget's instance id for an ability card, the item INSTANCE for an item card. A fan whose
///      contents have not changed rebuilds nothing at all, so the steady state of three peers with
///      twelve cards each is a few dozen early-returns per cadence tick and zero allocation.
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire. It draws faces for cards whose identities are
/// read locally off the host-replicated actor, gated by <see cref="RevealGate"/>. Card and item
/// IDENTITY remain DELIBERATELY-NOT on the wire. See INVARIANTS-Net-Rig.md "Net — content
/// classification".</remarks>
internal sealed class RemotePileFronts
{
    /// <summary>Which of the peer's piles the fan this driver serves is currently showing.</summary>
    internal enum Content
    {
        /// <summary>Their discard pile (<c>CCharacterClass.DiscardedAbilityCards</c>).</summary>
        Discard,

        /// <summary>Their burnt pile (lost + permanently lost — the union the 2D hand shows).</summary>
        Burnt,

        /// <summary>Their equipped items (<c>CInventory.AllItems</c>).</summary>
        Items,
    }

    private readonly RemoteAvatar _owner;

    /// <summary>Human name of the surface for the log line ("pile browse fan" / "item fan").</summary>
    private readonly string _surface;

    /// <summary>One overlay per slab, index-aligned with the fan's own slab list.</summary>
    private readonly List<RemoteCardArt> _arts = new(16);

    /// <summary>Reused resolve buffers — the per-cadence resolve allocates nothing.</summary>
    private readonly List<AbilityCardUI> _abilityBuf = new(16);
    private readonly List<CItem> _itemBuf = new(16);

    private float _nextResolveAt;
    private int _resolvedCount = -1;
    private int _resolvedContent = -1;
    private int _frontCount;

    /// <summary>Change key for <see cref="Log"/> — the packed (gate, content, fronts, slabs) word
    /// PLUS the stable id of the character the fan is about. The actor is part of the state a
    /// "welcher Character?" question is answered from: a peer switching focus between two teammates
    /// whose discard piles happen to hold the same number of cards is still a change, and without
    /// the id the log would go silent across exactly that switch.</summary>
    private (int Key, int ActorId) _loggedKey = (int.MinValue, 0);

    /// <summary>Why the front layer is (not) drawing — a CODE rather than a sentence, so the
    /// per-frame path can change-gate the diagnostic without composing a string. The sentence is
    /// re-derived once, in <see cref="Reason"/>, on the frames the line actually fires.</summary>
    private enum Gate
    {
        /// <summary>Fronts are permitted and a source resolved.</summary>
        Open,

        /// <summary>Fronts are permitted but the peer's pile is empty / no widget resolved.</summary>
        NoSource,

        /// <summary>No displayed character for this peer yet.</summary>
        NoActor,

        /// <summary>Not in a running scenario.</summary>
        OffScenario,

        /// <summary>The game's own secret selection window — THE one rule that hides fronts.</summary>
        SecretPhase,

        /// <summary>A game read threw; treated exactly like a shut gate.</summary>
        Errored,
    }

    private static string Reason(Gate gate) => gate switch
    {
        Gate.Open => "RevealGate.ShowRoundCardFronts(actor)=true",
        Gate.NoSource => "RevealGate.ShowRoundCardFronts(actor)=true, but the peer's pile resolved to " +
                         "no card widgets on this client",
        Gate.NoActor => "no displayed actor (join-time / benched peer)",
        Gate.OffScenario => "RevealGate.InScenario=false",
        Gate.SecretPhase => "RevealGate.ShowRoundCardFronts(actor)=false — the game's own secret " +
                            "SelectAbilityCardsOrLongRest phase for a remote actor",
        _ => "a game read threw; treated as a shut gate (fail-safe = no face)",
    };

    internal RemotePileFronts(RemoteAvatar owner, string surface)
    {
        _owner = owner;
        _surface = surface;
    }

    /// <summary>
    /// (Re)bind one overlay per slab. Called from the fan's own rebuild, which is the only place the
    /// slab list changes; the overlays are children of the slabs, so a slab that is destroyed takes
    /// its overlay's host with it — going through <see cref="RemoteCardArt.Destroy"/> first keeps the
    /// "we own the clone, we destroy the clone" contract explicit rather than relying on hierarchy
    /// destruction order.
    /// </summary>
    internal void Rebuild(List<GameObject> slabs, float cardWidth, float cardHeight)
    {
        for (int i = _arts.Count - 1; i >= 0; i--)
            _arts[i].Destroy();
        _arts.Clear();
        _frontCount = 0;
        Reset();

        if (slabs == null)
            return;
        for (int i = 0; i < slabs.Count; i++)
        {
            GameObject slab = slabs[i];
            if (slab != null)
                _arts.Add(new RemoteCardArt(slab.transform, cardWidth, cardHeight));
        }
    }

    /// <summary>Hide every face (fan closed / hidden by the remote-board setting) without destroying
    /// the overlays — the fan reuses its slabs, so the next open re-resolves into the same hosts.</summary>
    internal void HideAll()
    {
        if (_frontCount == 0 && _resolvedCount < 0)
            return; // already quiet — free to call every frame
        for (int i = 0; i < _arts.Count; i++)
            _arts[i].HideFront();
        _frontCount = 0;
        Reset();
    }

    /// <summary>Destroy every overlay (fan teardown).</summary>
    internal void Destroy()
    {
        for (int i = _arts.Count - 1; i >= 0; i--)
            _arts[i].Destroy();
        _arts.Clear();
        _abilityBuf.Clear();
        _itemBuf.Clear();
        _frontCount = 0;
        Reset();
    }

    private void Reset()
    {
        _resolvedCount = -1;
        _resolvedContent = -1;
        _nextResolveAt = 0f;
        // The log key deliberately survives a Reset: it tracks what was last SAID, not what was last
        // resolved, so a fan that closes and reopens in the same state does not re-announce itself.
    }

    /// <summary>
    /// Per-frame entry point — <paramref name="content"/> is which of the peer's piles the fan is
    /// currently showing (constant for the item fan, the wire's pile kind for the browse fan).
    ///
    /// ANTI-CHEAT ORDER OF OPERATIONS: the gate is evaluated BEFORE any face object is touched, and a
    /// closed gate takes the hide path — there is no arrangement in which a face exists for a frame
    /// ahead of the gate, because <see cref="RemoteCardArt"/> builds its clone under an INACTIVE host
    /// and only activates it inside the front branch below.
    /// </summary>
    internal void Tick(Content content)
    {
        if (_arts.Count == 0)
            return;

        // ---- THE GATE, EVERY FRAME ------------------------------------------------------------
        // Deliberately NOT on the content cadence below. The cadence exists to keep the expensive
        // RESOLVE off the per-frame path; putting the gate on it too would leave up to a quarter of a
        // second of fronts standing after the secret selection phase opens, and a leak window that
        // exists "only for 250 ms" is still a leak window. The gate itself is a handful of property
        // reads, so it costs nothing to ask it honestly.
        CPlayerActor? actor = null;
        Gate gate;
        try
        {
            // WHICH character: the one the peer's board is DISPLAYING (RemoteBoardFocus), not
            // necessarily the one they own — the same resolution the hand fan and the board itself
            // use, so a peer reading a teammate's pile shows that teammate's cards rather than a
            // mismatched pair. It falls back to their owned character whenever the focus is absent,
            // unresolvable or suppressed.
            actor = RemoteBoardFocus.DisplayedActor(_owner, out _);
            if (actor == null)
                gate = Gate.NoActor;
            else if (!RevealGate.InScenario)
                // The clone paths lean on scenario singletons (CardsHandManager, ObjectPool's card
                // pools); off-scenario there is no peer board to hang a fan off either.
                gate = Gate.OffScenario;
            else
                gate = RevealGate.ShowRoundCardFronts(actor) ? Gate.Open : Gate.SecretPhase;
        }
        catch (System.Exception ex)
        {
            // ANY failure → backs only. Fail-safe is "no face", never "a face we could not verify".
            actor = null;
            gate = Gate.Errored;
            VRLog.Debug("Net", $"Remote {_surface} [player {_owner.PlayerId}] reveal gate errored " +
                               $"({ex.Message}) — backs.");
        }

        int contentKey = (int)content;
        if (gate != Gate.Open)
        {
            // Shut gate: tear every face down THIS frame, then say so once.
            if (_frontCount > 0 || _resolvedCount >= 0)
            {
                for (int i = 0; i < _arts.Count; i++)
                    _arts[i].HideFront();
                _frontCount = 0;
                Reset();
            }
            Log(content, gate, 0, actor);
            return;
        }

        // ---- THE RESOLVE, ON THE CONTENT CADENCE ----------------------------------------------
        bool due = contentKey != _resolvedContent
                   || _resolvedCount != _arts.Count
                   || Time.unscaledTime >= _nextResolveAt;
        if (!due)
        {
            // Steady state: the clones are already up and correct. All that is left is the mip-bake
            // upkeep for their ASYNC art, and that self-throttles to 1 Hz per overlay.
            for (int i = 0; i < _arts.Count; i++)
                _arts[i].MaintainMipBake();
            return;
        }
        _nextResolveAt = Time.unscaledTime + RemoteBoardContent.RefreshSeconds;

        // A pile switch on the same slabs must not carry the old pile's dedup keys across.
        if (contentKey != _resolvedContent && _resolvedContent >= 0)
        {
            for (int i = 0; i < _arts.Count; i++)
                _arts[i].HideFront();
            _frontCount = 0;
        }
        _resolvedContent = contentKey;

        bool resolved = false;
        try
        {
            resolved = Resolve(actor!, content);
        }
        catch (System.Exception ex)
        {
            resolved = false;
            _abilityBuf.Clear();
            _itemBuf.Clear();
            VRLog.Warn("Net", $"Remote {_surface} [player {_owner.PlayerId}] front resolve failed " +
                              $"({ex.Message}) — showing backs.");
        }

        int fronts = 0;
        for (int i = 0; i < _arts.Count; i++)
        {
            RemoteCardArt art = _arts[i];
            bool shown = false;
            if (resolved)
            {
                if (content == Content.Items)
                {
                    if (i < _itemBuf.Count)
                        shown = RemoteItemCardSource.ShowFace(art, _itemBuf[i]);
                }
                else if (i < _abilityBuf.Count)
                {
                    AbilityCardUI widget = _abilityBuf[i];
                    FullAbilityCard? full = widget != null ? widget.fullAbilityCard : null;
                    shown = full != null && art.ShowFront(full);
                }
            }
            if (shown)
                fronts++;
            else
                art.HideFront();
        }
        _frontCount = fronts;
        _resolvedCount = _arts.Count;

        Log(content, resolved ? Gate.Open : Gate.NoSource, fronts, actor);
    }

    /// <summary>
    /// Fill the reused buffer for <paramref name="content"/> off the peer's OWN replicated model.
    /// Returns false when there is nothing to draw (which lands the fan on backs, exactly as before).
    ///
    /// <para>SLOT ALIGNMENT: slab <c>i</c> takes model entry <c>i</c>. The two agree whenever the arc
    /// holds the whole pile, which is the normal case — the sender's own arc is built from this same
    /// list in this same order. They can differ by one for as long as the owner has physically PLUCKED
    /// a card out of their arc (the wire count drops, the model list does not), which shifts the tail
    /// of the arc by one card until they put it back. That is a cosmetic mismatch inside a pile the
    /// viewer is allowed to read in full, never a disclosure; buying exactness would cost a per-card
    /// wire field, which is the one thing this feature is built to avoid.</para>
    /// </summary>
    private bool Resolve(CPlayerActor actor, Content content)
    {
        _abilityBuf.Clear();
        _itemBuf.Clear();

        if (content == Content.Items)
        {
            CInventory? inv = actor.Inventory;
            List<CItem>? all = inv != null ? inv.AllItems : null;
            if (all == null)
                return false;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null)
                    _itemBuf.Add(all[i]);
            }
            return _itemBuf.Count > 0;
        }

        CardsHandManager manager = CardsHandManager.Instance;
        if (manager == null)
            return false;
        CardsHandUI hand = manager.GetHand(actor);
        if (hand == null)
            return false;
        // The owner's own browse arc is filled from exactly this call, in exactly this order
        // (CardsDriver → CardsGameApi.GetPileWidgets), so the mirrored arc is card-for-card theirs.
        CardsGameApi.GetPileWidgets(hand, content == Content.Burnt, _abilityBuf);
        return _abilityBuf.Count > 0;
    }

    /// <summary>
    /// One change-gated line per surface stating FRONTS vs BACKS and WHICH <see cref="RevealGate"/>
    /// call decided it (grep: "Remote fan faces"). The point is that a single hardware log proves the
    /// phase rule on EVERY remote card surface at once — this line, the hand fan's, and the board's
    /// "Remote board content … fronts=" line all name the same predicate, so a surface that disagrees
    /// with the others is visible without a screenshot. Never per frame, never a card identity.
    /// </summary>
    private void Log(Content content, Gate gate, int fronts, CPlayerActor? actor)
    {
        // CHEAP CHANGE KEY FIRST. This method is on the per-frame path through the shut-gate branch,
        // so the line must not be COMPOSED unless it is going to be new — an interpolated string per
        // fan per peer per frame is exactly the kind of steady-state allocation this file exists to
        // avoid. Packing (gate, content, fronts, slabs) into one int plus the actor id is the whole
        // test; the tuple compare is a struct compare and allocates nothing.
        int key = ((int)gate << 24) | ((int)content << 20) | ((fronts & 0xFF) << 8) | (_arts.Count & 0xFF);
        int actorId = NetFigures.StableActorId(actor);
        if (key == _loggedKey.Key && actorId == _loggedKey.ActorId)
            return;
        _loggedKey = (key, actorId);

        string line = $"Remote {_surface} faces [player {_owner.PlayerId}]: " +
                      $"{(fronts > 0 ? "FRONTS" : "BACKS")} — content={content.ToString().ToUpperInvariant()} " +
                      $"of '{Board.CharacterFocus.Describe(actor)}' (the character this peer's board " +
                      "is presenting — record 22 via RemoteBoardFocus.DisplayedActor, so their pile " +
                      "fan follows their focus exactly as their own board does), " +
                      $"{fronts}/{_arts.Count} slab(s) show the real game card face, gate: {Reason(gate)}";
        VRLog.Info("Net", line + ". Card/item identities are read LOCALLY from the peer's " +
                          "host-replicated model (CCharacterClass piles / Inventory.AllItems) and never " +
                          "from a packet — the reveal rule is a PHASE, not a place (user ruling " +
                          "2026-08-08): backs everywhere during SelectAbilityCardsOrLongRest, fronts " +
                          "everywhere otherwise. 'FRONTS' with fewer slabs than cards means a widget " +
                          "could not be resolved for the rest and they stay backs — the safe direction.");
    }
}
