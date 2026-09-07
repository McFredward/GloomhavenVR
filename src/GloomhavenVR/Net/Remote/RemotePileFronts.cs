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
///      <see cref="RemoteCardArt.MaintainMipBake"/>, whose mip-bake and driver-resolution halves both
///      early-return on their own 1 s cadences. The ONE thing it does per frame is re-ask the
///      face-hosting verdict against the owning peer board's live composite state — a reference
///      compare, a bool read and a change-gated mesh assignment — which has to run at the frame rate
///      because a ~0.6 s see-through ramp read at 1 Hz would hand a peer's card its depth stamp back
///      somewhere in the middle of the NEXT episode (see RemoteCardArt.MaintainBodyFaceHosting).
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
    /// <summary>
    /// THE BOARD FANS' READING PITCH, in degrees about their own local X — the trailing
    /// <c>* Quaternion.Euler(-12f, 0f, 0f)</c> that both LOCAL board fans append to their head
    /// billboard (<c>PileBrowser.Tick</c> / <c>PileBrowser.PlaceAtHead</c> and
    /// <c>ItemsPile.FaceHead</c>, both commented "+Z points away from the viewer; tilt back a
    /// touch"). It is a LITERAL on the owner's side too — no config entry, nothing on the wire —
    /// which is why it is a shared constant here rather than a dial.
    ///
    /// <para>WHY IT LIVES IN THIS FILE. Both mirrors billboarded with a bare
    /// <c>LookRotation(away, Vector3.up)</c> and dropped the trailing term, so a peer's item fan and
    /// a peer's browse fan both stood <b>12 degrees more upright</b> than the arcs their owner was
    /// reading — at the shipped defaults, with nobody having tuned anything. Two independent copies
    /// of the number is how that gap comes back, and this class is already the one type both fans
    /// share (it draws the faces for exactly these two arcs), so the mirrors read it from here.</para>
    ///
    /// <para>IT IS NO LONGER A LITERAL, and that is a strict improvement of the same shape
    /// <c>RemoteBoardFurniture.PinIdleColor</c> took: it is the OWNER's own
    /// <c>Cards.PileFanShape.ReadingPitchDegrees</c>, so the four arcs that must agree — two owner
    /// fans and their two mirrors — read one definition instead of two that happen to match.</para>
    /// </summary>
    internal const float FanReadingPitchDegrees = Cards.PileFanShape.ReadingPitchDegrees;

    /// <summary>
    /// THE BOARD FANS' ARCH DEPTH — the <c>0.55</c> in <c>(cos(rad) - 1) * radius * 0.55f</c>, which
    /// <c>PileBrowser.Relayout</c> and <c>ItemsPile.Relayout</c> both apply to bend their arc around
    /// a pivot BELOW the root without dropping the ends the full sagitta.
    ///
    /// <para>DELIBERATELY NOT <c>Defaults.FanFlatCurvatureFactor</c>. That entry is the HAND fan's
    /// dial (<c>CardFan</c> reads <c>[Cards] FanFlatCurvatureFactor</c> live); the two board pile
    /// fans have no config entry at all. The numbers are equal today and they are not the same
    /// number — pointing this constant at that entry would make a hand-fan retune silently reshape
    /// two arcs the owner's own retune leaves alone, which is the "wrong entry" mistake
    /// scripts/check-remote-defaults.py exists to catch. If the owner's pile arcs ever gain a dial,
    /// this becomes a wire-overridable field like the radius beside it.</para>
    ///
    /// <para>IT IS ALSO NO LONGER A LITERAL HERE: the owner's two arcs used to spell 0.55 inline in
    /// <c>PileBrowser.Relayout</c> and <c>ItemsPile.Relayout</c> — inline, so no checker in the
    /// project could see a retune of it — and it now has one home on the owner's side
    /// (<c>Cards.PileFanShape.ArchFactor</c>) that this reads.</para>
    /// </summary>
    internal const float FanArchFactor = Cards.PileFanShape.ArchFactor;

    /// <summary>The board fans' ROLL gain — the <c>0.85</c> in <c>Euler(0, 0, -angle * 0.85f)</c>,
    /// which both <c>PileBrowser.Relayout</c> and <c>ItemsPile.Relayout</c> apply so a card leans
    /// slightly less than its own arc angle. Read off the owner's own home for the same reason
    /// <see cref="FanArchFactor"/> is — and it is still NOT <c>Defaults.FanTiltFactor</c>, which is
    /// the hand fan's live dial.</summary>
    internal const float FanTiltFactor = Cards.PileFanShape.TiltFactor;

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

    /// <summary>
    /// The slab ROOTS the overlays are bound to, index-aligned with <see cref="_arts"/> — the
    /// handle <see cref="SetFrontFace"/> needs, kept here because a slab's own body is
    /// <c>RemoteCardArt</c>'s business and its material is not.
    ///
    /// <para>USER ITEM 10 (2026-09-06): a slab showing a card FRONT must not still be WEARING the
    /// card back on its front fan, or every part of the slab the print does not paint — the
    /// banner, the outer frame, the scalloped crest — shows the back's burgundy/gold lattice
    /// around a readable peer card. See <c>CardMesh.SetBodyFrontFace</c>, which owns the rule and
    /// the write; this list is only how the rule is told WHICH body.</para>
    /// </summary>
    private readonly List<Transform> _slabs = new(16);

    /// <summary>What each seat's front fan is currently WEARING (true = the card back), so the
    /// per-seat drive below is an EDGE and not a per-frame walk of <c>CardMesh</c>'s body registry.
    /// Index-aligned with <see cref="_slabs"/>; seeded true because that is what
    /// <see cref="RemoteBrowserFan"/> and <see cref="RemoteItemFan"/> build their slabs with.</summary>
    private readonly List<bool> _wearsBack = new(16);

    /// <summary>Reused resolve buffers — the per-cadence resolve allocates nothing.</summary>
    private readonly List<AbilityCardUI> _abilityBuf = new(16);
    /// <summary>The ITEM resolve buffer: <c>Inventory.AllItems</c> with its null entries SKIPPED,
    /// which is the ARC's index space — no chip is built for a null on either machine. See
    /// <see cref="Resolve"/>, and <see cref="RemoteUsableFrame.ResolveSlots"/> for the translation
    /// record 35's raw mask index needs to reach it.</summary>
    private readonly List<CItem> _itemBuf = new(16);

    private float _nextResolveAt;
    private int _resolvedCount = -1;
    private int _resolvedContent = -1;

    /// <summary>The gate the faces currently up were resolved under, so a gate CHANGE forces the
    /// resolve on the very frame it happens instead of on the next cadence tick.
    ///
    /// <para>IT EXISTS BECAUSE <see cref="Gate.BurnException"/> ADDED A THIRD STATE TO A TEST THAT
    /// HAD TWO. Before it, every non-<see cref="Gate.Open"/> gate took the tear-down path per frame,
    /// so the class's own stated anti-leak property held by construction: "putting the gate on the
    /// cadence too would leave up to a quarter of a second of fronts standing after the secret
    /// selection phase opens, and a leak window that exists 'only for 250 ms' is still a leak
    /// window." An Open ⇒ BurnException transition does NOT take that path — it must not, the whole
    /// point is that the arc keeps drawing — so without this field the per-card ask would not run
    /// until the next cadence tick and the fronts standing in between would be the OLD gate's. That
    /// is the same window, reintroduced by the fix. -1 = nothing resolved.</para></summary>
    private int _resolvedGate = -1;

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

        /// <summary>
        /// THE SECRET WINDOW IS OPEN AND THIS IS THE BURNT PILE, so the gate is asked PER CARD
        /// instead of per fan (2026-09-07 review, item B2).
        ///
        /// <para>USER, VERBATIM, AND IT IS THE STRONGEST TERM HE HAS USED FOR ANY FACE: "Beim
        /// Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer mit der Vorderseite sichtbar
        /// sein." <c>RevealGate.IsPubliclyRevealedCard</c> owns that ruling and it is a property of
        /// the CARD — <c>RevealGate</c>'s own note says the exception exists as a card property
        /// "so every surface that can name the card it is drawing gets the ruling by asking". This
        /// surface could NAME every card it draws and never asked: it took the two-argument
        /// <c>RevealGate.CardFaces</c>, which cannot reach the exception, and drew a whole burnt
        /// arc as backs inside a peer's short rest. EVERY card in a burnt browse arc is in that
        /// character's <c>LostAbilityCards</c> / <c>PermanentlyLostAbilityCards</c> BY
        /// CONSTRUCTION, which is the same list the exception reads.</para>
        ///
        /// <para>ONLY THE BURNT ARM, AND ONLY THIS ONE REFUSAL. <see cref="Content.Discard"/> and
        /// <see cref="Content.Items"/> keep the whole-fan gate — a discarded card is not burnt and
        /// its identity is still the selection window's secret. And only
        /// <see cref="SecretPhase"/> converts: <see cref="NoActor"/>, <see cref="OffScenario"/> and
        /// <see cref="Errored"/> are CAPABILITY failures, not secrecy verdicts, and a capability
        /// failure cannot be argued away by a ruling about what may be shown.</para>
        ///
        /// <para>THE ANTI-CHEAT ORDERING IS UNTOUCHED. The per-card ask below still happens BEFORE
        /// any face object is shown, and a card that fails it is torn down on the same frame — the
        /// carve-out lets the resolve RUN, it does not let a face exist ahead of a permission.</para>
        /// </summary>
        BurnException,

        /// <summary>A game read threw; treated exactly like a shut gate.</summary>
        Errored,

        /// <summary>
        /// THE LENGTH-AGREEMENT BELT. Fronts are permitted and a source resolved, but this client's
        /// copy of the peer's pile is a DIFFERENT LENGTH from the arc the owner is actually looking
        /// at (the wire's slab count). The positional zip below is only a name for a card while the
        /// two lists agree; the moment they do not, every slab from the first divergence on draws
        /// somebody else's card. Backs instead — see <see cref="Tick"/>'s belt block for the whole
        /// argument.
        /// </summary>
        CountMismatch,
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
        Gate.BurnException => "RevealGate.ShowRoundCardFronts(actor)=false, but this is the BURNT " +
                              "pile, so the gate is asked PER CARD through the card-aware " +
                              "RevealGate.CardFaces overload: every card in this arc is in that " +
                              "character's Lost/PermanentlyLost lists by construction and " +
                              "RevealGate.IsPubliclyRevealedCard therefore opens it — user, " +
                              "verbatim: 'Beim Verbrennen EGAL AUS WELCHEM GRUND muss die Karte " +
                              "immer mit der Vorderseite sichtbar sein'. A BACK on this line is a " +
                              "card the exception refused, which for this arc is a finding",
        Gate.CountMismatch => "RevealGate.ShowRoundCardFronts(actor)=true, but this client's copy of " +
                              "the peer's pile is a different LENGTH from the arc they are looking " +
                              "at — a positional zip across a length disagreement draws the WRONG " +
                              "card's face, so every slab stays a back until the two agree",
        _ => "a game read threw; treated as a shut gate (fail-safe = no face)",
    };

    /// <summary>This surface's standing verdict for the per-population census — every frame, on
    /// every path, including the ones that draw nothing. See <see cref="PeerCardFaceCensus"/> for
    /// why the change-gated <c>Log</c> beside it could not answer the question the user is asking.
    /// The population follows the CONTENT, because a peer's item fan and their pile-browse arc are
    /// two different pictures that happen to share this driver.</summary>
    private void Census(Content content, int fronts, Gate gate) =>
        PeerCardFaceCensus.Report(
            content == Content.Items
                ? PeerCardFaceCensus.Surface.ItemFan
                : PeerCardFaceCensus.Surface.PileBrowse,
            _owner.PlayerId, fronts, System.Math.Max(DrawnSlabs() - fronts, 0),
            $"{content}: {Reason(gate)}");

    /// <summary>How many of this fan's slabs are actually ON SCREEN. It used to be
    /// <c>_arts.Count</c> inline, which was the same number until <c>RemoteItemFan</c> gained a
    /// reason to deactivate one (the chip in the owner's fist, 2026-09-06 report item 5): a slab
    /// nobody can see is neither a front nor a back, and counting it as a BACK would have this
    /// census reporting a 1:1 breach that does not exist. Bounded by the arc size.</summary>
    private int DrawnSlabs()
    {
        int n = 0;
        for (int i = 0; i < _arts.Count; i++)
        {
            if (_arts[i].HostDrawn)
                n++;
        }
        return n;
    }

    /// <summary>The renderer stack at this fan's first DRAWN seat — see
    /// <see cref="RemoteCardArt.DescribeStack"/>, which is the one implementation of that walk. The
    /// arc's own log line quotes it, so the membership reading and the overlay reading arrive in one
    /// line for one seat rather than as two lines a reader has to pair up by timestamp.</summary>
    internal string DescribeFirstDrawnSeat()
    {
        for (int i = 0; i < _arts.Count; i++)
        {
            if (_arts[i].HostDrawn)
                return _arts[i].DescribeStack();
        }
        return "SEAT STACK: no drawn seat in this arc.";
    }

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
        _slabs.Clear();
        _wearsBack.Clear();
        _frontCount = 0;
        Reset();

        if (slabs == null)
            return;
        for (int i = 0; i < slabs.Count; i++)
        {
            GameObject slab = slabs[i];
            if (slab == null)
                continue;
            _arts.Add(new RemoteCardArt(slab.transform, cardWidth, cardHeight));
            _slabs.Add(slab.transform);
            _wearsBack.Add(true);
        }
    }

    /// <summary>
    /// Tell seat <paramref name="index"/>'s card BODY what its front fan is wearing — user item 10.
    /// Idempotent all the way down (<c>CardMesh.SetBodyFrontFace</c> compares against the authored
    /// material and writes nothing when it already matches), so it is safe on the per-seat path.
    /// </summary>
    private void SetFrontFace(int index, bool showsBack)
    {
        if (index < 0 || index >= _slabs.Count || index >= _wearsBack.Count)
            return;
        if (_wearsBack[index] == showsBack)
            return; // an EDGE, so a steady arc never walks CardMesh's body registry at all
        Transform slab = _slabs[index];
        if (slab == null)
            return;
        CardMesh.SetBodyFrontFace(slab, showsBack);
        _wearsBack[index] = showsBack;
    }

    /// <summary>Hand every seat's front fan back to the card BACK — the state a slab with no print
    /// on it must be in, and therefore the state every path that tears fronts down has to leave
    /// behind. Named once so the four such paths cannot drift apart.</summary>
    private void ShowBacksEverywhere()
    {
        for (int i = 0; i < _slabs.Count; i++)
            SetFrontFace(i, showsBack: true);
    }

    /// <summary>Hide every face (fan closed / hidden by the remote-board setting) without destroying
    /// the overlays — the fan reuses its slabs, so the next open re-resolves into the same hosts.</summary>
    internal void HideAll()
    {
        if (_frontCount == 0 && _resolvedCount < 0)
            return; // already quiet — free to call every frame
        for (int i = 0; i < _arts.Count; i++)
            _arts[i].HideFront();
        ShowBacksEverywhere();
        _frontCount = 0;
        Reset();
    }

    /// <summary>Destroy every overlay (fan teardown).</summary>
    internal void Destroy()
    {
        for (int i = _arts.Count - 1; i >= 0; i--)
            _arts[i].Destroy();
        _arts.Clear();
        _slabs.Clear();
        _wearsBack.Clear();
        _abilityBuf.Clear();
        _itemBuf.Clear();
        _frontCount = 0;
        Reset();
    }

    private void Reset()
    {
        _resolvedCount = -1;
        _resolvedContent = -1;
        _resolvedGate = -1;
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
            // THE ONE CALL, not a fourth hand-written copy of its terms. This used to spell
            // `InScenario ? (ShowRoundCardFronts ? Open : Secret) : OffScenario` — which is the same
            // conjunction RevealGate.CardFaces was extracted to own, re-derived here, and therefore
            // one more place for the two halves to come apart (RevealGate's own doc block names that
            // as the ModBuild-192 defect and says it recurred once already). The mapping back onto
            // this class's Gate enum is exact and deliberately loses nothing: MapLoadout means "no
            // scenario is running", and a pile-browse arc or an item fan needs the scenario
            // singletons its clone path reads, so for THIS surface that answer IS OffScenario.
            RevealGate.CardFaceSource source =
                RevealGate.CardFaces(RevealGate.PeerCardPopulation.Selectable, actor);
            if (actor == null)
                gate = Gate.NoActor;
            else if (source == RevealGate.CardFaceSource.Scenario)
                gate = Gate.Open;
            else if (source == RevealGate.CardFaceSource.MapLoadout || !RevealGate.InScenario)
                gate = Gate.OffScenario;
            else
                gate = Gate.SecretPhase;
        }
        catch (System.Exception ex)
        {
            // ANY failure → backs only. Fail-safe is "no face", never "a face we could not verify".
            actor = null;
            gate = Gate.Errored;
            VRLog.Debug("Net", $"Remote {_surface} [player {_owner.PlayerId}] reveal gate errored " +
                               $"({ex.Message}) — backs.");
        }

        // ─── THE BURN EXCEPTION IS A CARD PROPERTY, SO THE BURNT ARC ASKS PER CARD ─────────────
        // See Gate.BurnException. The whole-fan refusal below is right for the discard and item
        // arcs and wrong for this one, and it was wrong in the direction the user has now ruled on
        // three times. Converting the gate here rather than adding a second early-out keeps ONE
        // shut path and ONE open path; the per-card ask is in the resolve loop, where the card is
        // in hand.
        if (gate == Gate.SecretPhase && content == Content.Burnt && actor != null)
            gate = Gate.BurnException;

        int contentKey = (int)content;
        if (gate != Gate.Open && gate != Gate.BurnException)
        {
            // Shut gate: tear every face down THIS frame, then say so once.
            if (_frontCount > 0 || _resolvedCount >= 0)
            {
                for (int i = 0; i < _arts.Count; i++)
                    _arts[i].HideFront();
                ShowBacksEverywhere();
                _frontCount = 0;
                Reset();
            }
            Census(content, 0, gate);
            Log(content, gate, 0, actor);
            return;
        }

        // ---- THE RESOLVE, ON THE CONTENT CADENCE ----------------------------------------------
        bool due = contentKey != _resolvedContent
                   || _resolvedCount != _arts.Count
                   || (int)gate != _resolvedGate
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
            ShowBacksEverywhere();
            _frontCount = 0;
        }
        _resolvedContent = contentKey;
        _resolvedGate = (int)gate;

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

        // ---- THE LENGTH-AGREEMENT BELT --------------------------------------------------------
        // A LENGTH DISAGREEMENT MEANS THE TWO SIDES ARE NOT LOOKING AT THE SAME PILE — SHOW BACKS.
        // (2026-09-02 multiplayer hardware report, item 5c: the co-player burnt a card and the peer
        // saw a DIFFERENT card marked burnt on his board.)
        //
        // The slab COUNT is the OWNER's, off the wire and timely: it is the size of the arc they are
        // physically reading. The FACES are this client's OWN walk of the host-replicated model. The
        // loop below then zips the two POSITIONALLY with nothing but a bounds check, which is a name
        // for a card only while the two lists agree entry for entry. They do not always agree, and
        // the doc on Resolve used to argue that every disagreement was a TAIL truncation — harmless,
        // because a prefix of a prefix is still aligned. That argument is FALSE and this is where it
        // dies:
        //
        //   * THE BURNT PILE IS A CONCATENATION. CardsGameApi.GetPileWidgets appends
        //     LostAbilityCards and then PermanentlyLostAbilityCards. A card the owner has just burnt
        //     is appended to the FIRST segment, i.e. it lands in the MIDDLE of the concatenated list,
        //     and every permanently-lost entry behind it shifts by one seat.
        //   * THE OWNER'S ARC SKIPS IN THE MIDDLE TOO. CardsDriver.UpdateBrowser drops a widget with
        //     no AbilityCard or with IsLongRest, and then drops any card whose VR visual the board is
        //     still holding or that is still flying in (BoardOwnsCardVisual / CardEnRouteToPile).
        //     Those are positional skips inside GetPileWidgets order, not a truncation of its tail.
        //
        // So the pre-belt behaviour was not "the last card is missing", it was "from the burnt card
        // on, every slab shows its neighbour's face" — for up to BurnEffectMaxHoldSeconds (3 s), and
        // the remote log measured it AT that ceiling. A back reads to the player as "not loaded yet"
        // and costs him nothing. A WRONG FRONT breaks the 1:1 rule and is unrecoverable, because he
        // believes it and acts on it. The disagreement of the two lengths is the observable proof of
        // exactly the condition that makes the zip unsafe, so it is the right term to gate on.
        //
        // This is a BELT, deliberately kept even after Resolve was taught the owner's own filter:
        // the residual divergence (a card whose visual is still on the owner's board or in flight) is
        // sender-local VR state that has no representation on this client at all, so no amount of
        // model reading can reproduce it. The belt is what turns that residue from a wrong front into
        // a back.
        int modelCount = content == Content.Items ? _itemBuf.Count : _abilityBuf.Count;
        if (resolved && modelCount != _arts.Count)
        {
            for (int i = 0; i < _arts.Count; i++)
                _arts[i].HideFront();
            ShowBacksEverywhere();
            _frontCount = 0;
            _resolvedCount = _arts.Count;
            Census(content, 0, Gate.CountMismatch);
            Log(content, Gate.CountMismatch, 0, actor);
            return;
        }

        int fronts = 0;
        // Slabs carrying a settled card-FX look this pass — the burnt arc's char and, since
        // ModBuild 479, the discard arc's grey.
        int fxLooks = 0;
        // Slabs the PER-CARD burn exception refused. Zero is the expected reading (every card in a
        // burnt arc is in the owner's lost lists by construction), so a non-zero one is the
        // falsifier for that sentence and not a footnote.
        int carvedBacks = 0;
        for (int i = 0; i < _arts.Count; i++)
        {
            RemoteCardArt art = _arts[i];
            bool shown = false;
            // A SLAB THAT IS NOT DRAWN IS NOT PART OF THIS COUNT. RemoteItemFan deactivates the slab
            // of a chip the owner is holding in their fist, so that slot is on nobody's screen;
            // resolving a face onto it would cost a clone for nothing and — the reason this test is
            // here rather than left implicit — would be counted by Census below, making the one
            // instrument that measures the 1:1 face rule report a card the player cannot see.
            //
            // ITS EXISTING FACE IS LEFT ALONE, DELIBERATELY. Tearing it down would be free while the
            // host is hidden and expensive the moment it comes back: this resolve runs on the
            // board-content cadence, so a chip released back into the arc would GLIDE HOME AS A BACK
            // for up to a quarter of a second before the next tick re-clothed it, in an arc of
            // fronts. The clone is invisible under an inactive host either way, and index i names
            // the same item for as long as the count is stable — which is precisely when a rebuild
            // does not happen. Secrecy is untouched: a shut gate still tears every face down above,
            // before this loop is reached.
            bool drawn = art.HostDrawn;
            if (resolved && drawn)
            {
                if (content == Content.Items)
                {
                    if (i < _itemBuf.Count)
                        shown = RemoteItemCardSource.ShowFace(art, _itemBuf[i]);
                }
                else if (i < _abilityBuf.Count)
                {
                    AbilityCardUI widget = _abilityBuf[i];
                    // THE PER-CARD HALF OF Gate.BurnException, asked BEFORE any face is shown. On
                    // the ordinary open path this is not asked at all — the fan-wide gate already
                    // answered — so the steady state costs nothing; under the carve-out it is one
                    // walk of two host-replicated lists per slab per cadence tick.
                    bool permitted = gate != Gate.BurnException
                        || (widget != null
                            && RevealGate.CardFaces(RevealGate.PeerCardPopulation.Selectable, actor,
                                                    widget.CardInstanceID)
                               != RevealGate.CardFaceSource.None);
                    if (!permitted)
                        carvedBacks++;
                    FullAbilityCard? full =
                        permitted && widget != null ? widget.fullAbilityCard : null;
                    shown = full != null && art.ShowFront(full);
                    // A CARD IN THE BURNT PILE IS A BURNT CARD, AND IT LOOKS LIKE ONE (2026-09-06
                    // report, item 9b: "Die verbrannt-animation die über der Karte liegt ist bei
                    // mir sichtbar aber nicht bei der remote Karte").
                    //
                    // The owner's own burnt pile hosts the REAL widget, which the game painted
                    // through CardEffects.BurnCardTimeline and left in its settled end-state. The
                    // mirror hosts a CLONE of that widget — and a clone of a widget that never ran
                    // Initialize on this client carries neither the material state nor the text
                    // recolour, so the peer's burnt pile was a fan of FRESH cards. Nothing was
                    // asked for it: SetAbilityBurnProgress had exactly ONE caller in the whole mod
                    // (RemoteBurnFx, the 2 s flight), so the pile the card lands in never got it.
                    //
                    // t = 1 and NOT a ramp, and this is the one place ApplySpentLook's rule applies
                    // rather than RemoteBurnFx's: a peer's pile fan opens whenever the viewer wants
                    // to look at it, which is usually long after the owner's timeline ran, and a
                    // burn STARTING then would be a picture the owner never had. RemoteBurnFx owns
                    // the animation at the moment of the burn; this owns the settled look for the
                    // rest of the scenario. The write is idempotent (the same floats, the same
                    // colours) and the rig itself is built once per clone, so the 4 Hz cadence this
                    // sits on costs a handful of SetFloat calls per slab and nothing else.
                    //
                    // ─── AND SO IS A DISCARDED CARD (ModBuild 479, user item 2a) ─────────────────
                    // THE SENTENCE THAT USED TO STAND HERE WAS FALSE, and it is written out rather
                    // than deleted because it is the whole of the defect: "DISCARD IS DELIBERATELY
                    // NOT INCLUDED: a discarded card is not burnt and the owner's own discard fan
                    // shows it fresh." The second clause is the claim, and the game contradicts it
                    // in one line — FullAbilityCard.SetPile(ECardPile.Discarded) calls
                    // cardEffects.ToggleEffect(active: true, FXTask.DiscardMode)
                    // (FullAbilityCard.cs:316-319), which runs GhostOutOnTimeline and greys the
                    // WHOLE card. Every card in the owner's own discard fan is wearing that ghost.
                    //
                    // The user reported it as the mirror's defect, verbatim: "Öffnet ein Spieler
                    // seine abgeworfenen Karten sehe ich dort die Standartfarben ohne
                    // ausgegraut/braun - der Spieler sieht es richtig ausgegraut."
                    //
                    // THE LOOK COMES FROM THE CARD, NOT FROM THE FAN'S IDENTITY. Asking
                    // Cards.BurnLookPolicy.ForCard rather than switching on `content` here means
                    // this surface holds no copy of the rule at all: the card's own
                    // CBaseCard.CurrentCardPile decides, which is Lost/PermanentlyLost for every
                    // member of a burnt arc and Discarded for every member of a discard arc BY
                    // CONSTRUCTION. If the two ever disagree the answer degrades to None — a fresh
                    // card, today's picture — rather than to a confident wrong look, which is the
                    // same safe direction every other mirrored surface takes.
                    //
                    // The t = 1 argument above holds for the grey-out word for word.
                    if (shown)
                    {
                        // Name the surface BEFORE the drive: the card-FX instrument latches per
                        // surface (RemoteCardArt.FxSurface) and this is the surface user item 9b
                        // (burnt) and user item 2a (discard) were both reported against.
                        art.Surface = RemoteCardArt.FxSurface.Pile;
                        RemoteCardArt.CardFxLook want =
                            UsedCardLook.FromState(full != null ? full.AbilityCard : null);
                        if (want == RemoteCardArt.CardFxLook.None)
                            art.ClearAbilityCardFx();
                        else if (art.SetAbilityCardFxProgress(want, 1f))
                            fxLooks++;
                    }
                }
            }
            if (shown)
                fronts++;
            else if (drawn)
                art.HideFront();
            // USER ITEM 10, per seat and at the same moment the face decision is made: a slab that
            // is showing a card FRONT stops wearing the card BACK on its front fan, so the parts of
            // it the print does not paint (the banner, the outer frame, the scalloped crest) read
            // as the OWNER'S OWN card edge instead of the back's burgundy/gold lattice.
            //
            // AN UNDRAWN SEAT IS LEFT ALONE, for the identical reason its FACE is (see the comment
            // at `drawn` above): RemoteItemFan deactivates the slab of a chip its owner is holding,
            // so nothing of it is on screen either way — and re-clothing it as a BACK here would
            // hand the lattice back for the quarter second between the chip returning to the arc
            // and the next cadence tick re-resolving its front. The material and the print now
            // follow one condition instead of two.
            if (drawn)
                SetFrontFace(i, showsBack: !shown);
        }
        _frontCount = fronts;
        _resolvedCount = _arts.Count;

        Gate outcome = !resolved ? Gate.NoSource
                     : gate == Gate.BurnException ? Gate.BurnException
                     : Gate.Open;
        Census(content, fronts, outcome);
        Log(content, outcome, fronts, actor);
        if (gate == Gate.BurnException && carvedBacks > 0)
            LogBurnCarveRefusal(carvedBacks, fronts, actor);
        if (content != Content.Items)
            LogPileLook(content, fxLooks, fronts, actor);
    }

    /// <summary>Change key for <see cref="LogBurnCarveRefusal"/>, so the line fires on a real edge
    /// and not on the 4 Hz cadence.</summary>
    private (int Key, int ActorId) _loggedCarveRefusal = (int.MinValue, 0);

    /// <summary>
    /// THE FALSIFIER FOR THIS CLASS'S BURN CARVE-OUT (<see cref="Gate.BurnException"/>). Grep token
    /// <c>BURNT ARC CARVE-OUT REFUSED</c>.
    ///
    /// <para>The carve-out rests on ONE claim: every card in a peer's burnt browse arc is in that
    /// character's <c>LostAbilityCards</c> or <c>PermanentlyLostAbilityCards</c>, which is the very
    /// membership <c>RevealGate.IsPubliclyRevealedCard</c> tests, so the per-card ask can only ever
    /// say yes. This line fires exactly when it said no — i.e. when the claim is false — and it is
    /// the only way to find that out, because a refused slab draws the SAME card back the old
    /// whole-fan gate drew and is invisible in every other reading.</para>
    ///
    /// <para>SILENCE IS THE EXPECTED READING, and it is not a "held instrument": the enclosing arm
    /// only runs while a viewer has a peer's BURNT pile open DURING that peer's secret selection
    /// window, so nothing at all is owed outside that intersection. The reading that means the
    /// carve-out is live and working is <c>Gate.BurnException</c> appearing in this surface's
    /// census line with a non-zero front count.</para>
    /// </summary>
    private void LogBurnCarveRefusal(int refused, int fronts, CPlayerActor? actor)
    {
        int key = (refused << 8) | (fronts & 0xFF);
        int actorId = NetFigures.StableActorId(actor);
        if (key == _loggedCarveRefusal.Key && actorId == _loggedCarveRefusal.ActorId)
            return;
        _loggedCarveRefusal = (key, actorId);
        // HW-VERIFY: grep token "BURNT ARC CARVE-OUT REFUSED" — see this method's doc.
        VRLog.Note("Net", $"BURNT ARC CARVE-OUT REFUSED [player {_owner.PlayerId}]: {refused} slab(s) "
                          + $"of this burnt arc stayed BACKS beside {fronts} front(s) for "
                          + $"'{Board.CharacterFocus.Describe(actor)}', inside that peer's secret "
                          + "selection window. The premise of the carve-out is that every card in a "
                          + "burnt browse arc is in that character's Lost/PermanentlyLost lists by "
                          + "construction, so RevealGate.IsPubliclyRevealedCard cannot refuse one. "
                          + "THIS LINE IS THAT PREMISE BEING FALSE. The two candidates are a widget "
                          + "with no model CAbilityCard behind it (CardsGameApi.PileWidgetIsArcMember "
                          + "should already have dropped it) and a length disagreement that slipped "
                          + "past the belt above — read the fan's own count line beside this one. "
                          + "The user's ruling is 'Beim Verbrennen EGAL AUS WELCHEM GRUND', so any "
                          + "occurrence is a finding and not a footnote.");
    }

    /// <summary>Change key for <see cref="LogPileLook"/>: the (content, looks, fronts) triple plus
    /// the character, so the line fires on a real edge and not on the 4 Hz cadence — and so the two
    /// ability arcs cannot suppress each other's reading.</summary>
    private (int Key, int ActorId) _loggedPileLook = (int.MinValue, 0);

    /// <summary>
    /// HARDWARE VERIFICATION (2026-09-06 report item 9b, and 2026-09-07 evening item 2a): does a
    /// peer's BURNT pile fan actually wear the char, and does their DISCARD fan wear the grey, or
    /// are they fans of fresh cards? Grep token <c>Remote pile look</c>.
    ///
    /// <para>WORKING = <c>looks</c> equal to <c>fronts</c> and both above 0 while the viewer has that
    /// peer's burnt pile open, beside a <c>Remote BURN look armed</c> line.</para>
    ///
    /// <para>INERT = <c>looks=0</c> with <c>fronts</c> above 0. The faces are up and not one of them
    /// took the look, which is precisely the 457 picture; the reason is then in
    /// <c>Remote BURN look</c> (REFUSED, or NO CARD-FX MATERIAL).</para>
    ///
    /// <para>STILL BEYOND THE INSTRUMENT = <c>looks</c> equal to <c>fronts</c> and the user still
    /// reports a fresh-looking card. The terms are being written and the divergence is in what they
    /// PAINT — the fgFx flame quad and the smoke emitter are not reproduced, and this line cannot
    /// see that.</para>
    /// </summary>
    private void LogPileLook(Content content, int looks, int fronts, CPlayerActor? actor)
    {
        int key = ((int)content << 16) | (looks << 8) | (fronts & 0xFF);
        int actorId = NetFigures.StableActorId(actor);
        if (key == _loggedPileLook.Key && actorId == _loggedPileLook.ActorId)
            return;
        _loggedPileLook = (key, actorId);
        string arc = content == Content.Burnt ? "burnt" : "discard";
        string look = content == Content.Burnt ? "settled burn" : "settled grey-out";
        // HW-VERIFY: grep token "Remote pile look" — see this method's doc for the three readings.
        VRLog.Note("Net", $"Remote pile look [player {_owner.PlayerId}] {arc} arc: {looks} of {fronts} "
                          + $"front(s) carry the {look} for '{Board.CharacterFocus.Describe(actor)}'. "
                          + "The owner's own fan hosts the REAL widget the game already painted — "
                          + "CardEffects.BurnCardTimeline for a lost card, GhostOutOnTimeline for a "
                          + "discarded one (FullAbilityCard.SetPile calls ToggleEffect for both, "
                          + ":313-322). This fan hosts a CLONE of that widget, and a clone whose "
                          + "source never ran CardEffects.Initialize on this client inherits neither "
                          + "the material state nor the text recolour. The settled end-state is "
                          + "therefore rebuilt here on materials this mod minted "
                          + "(RemoteCardArt.SetAbilityCardFxProgress(look, 1)), never on anything the "
                          + "game owns, and with NO animation — the owner's timeline ran long before "
                          + "this fan was opened. The look is the CARD's "
                          + "(Cards.BurnLookPolicy.ForCard), not this fan's, so looks < fronts on the "
                          + "discard arc also means a card in it does not read CurrentCardPile = "
                          + "Discarded. looks=0 beside fronts>0 is the defect, and 'Remote BURN look' "
                          + "says why.");
    }

    /// <summary>
    /// Fill the reused buffer for <paramref name="content"/> off the peer's OWN replicated model.
    /// Returns false when there is nothing to draw (which lands the fan on backs, exactly as before).
    ///
    /// <para>SLOT ALIGNMENT: slab <c>i</c> takes model entry <c>i</c>. The slab COUNT is the owner's,
    /// off the wire; the entries are this client's own walk of the same host-replicated pile. The zip
    /// is a name for a card only while the two lists agree entry for entry, so this method's whole job
    /// is to reproduce the owner's arc EXACTLY — and where it provably cannot, to let the length
    /// disagreement show, because <see cref="Tick"/>'s belt turns a disagreement into backs.</para>
    ///
    /// <para>THE ARGUMENT THAT USED TO STAND HERE WAS FALSE, and it is written out rather than
    /// deleted because it is the reason multiplayer report item 5c shipped. It ran: the owner's arc
    /// defers a card until its VR visual lands, but the game APPENDS to the pile lists, so an omitted
    /// entry is always the TAIL — slabs 0..n-1 still align with model entries 0..n-1 and nothing
    /// shifts. Both halves are wrong.
    /// <list type="bullet">
    ///   <item>THE BURNT PILE IS A CONCATENATION, not a list.
    ///         <c>CardsGameApi.GetPileWidgets</c> appends <c>LostAbilityCards</c> and THEN
    ///         <c>PermanentlyLostAbilityCards</c>. A normal burn appends to the FIRST segment, so it
    ///         lands in the MIDDLE of the concatenation and every permanently-lost entry behind it
    ///         moves down a seat. "The game appends" is true of each segment and says nothing about
    ///         their join.</item>
    ///   <item>THE OWNER'S ARC SKIPS IN THE MIDDLE. <c>CardsDriver.UpdateBrowser</c> drops non-member
    ///         widgets (<see cref="CardsGameApi.PileWidgetIsArcMember"/>) and then drops any card
    ///         whose visual its board is still holding or that is still flying in. Those are
    ///         positional skips inside <c>GetPileWidgets</c> order, not a truncation of its tail.</item>
    /// </list>
    /// The observed symptom was therefore not "the last card is missing" but "from the burnt card on,
    /// every slab draws its neighbour's face", for up to the 3 s burn hold.</para>
    ///
    /// <para>WHAT THIS METHOD DOES ABOUT IT, TERM BY TERM. The owner's arc filter has two terms and
    /// they are not the same KIND of thing:
    /// <list type="bullet">
    ///   <item>MEMBERSHIP (no model card / long rest) is pure host-replicated MODEL state, so this
    ///         client can evaluate it exactly. It is not re-implemented here — both sides call the one
    ///         expression, <see cref="CardsGameApi.PileWidgetIsArcMember"/>, because two copies kept in
    ///         step by hand is the defect and not the remedy. This term used to be applied by the owner
    ///         and NOT here at all, so a character holding a long-rest placeholder in the pile had the
    ///         whole mirrored arc one seat out permanently, not transiently.</item>
    ///   <item>ARRIVAL (the card's VR visual is still lying on the owner's board, still in flight, or
    ///         still held by its burn artwork) is SENDER-LOCAL VR state. It has no representation on
    ///         this client — not on the wire, not in the model — so no amount of model reading can
    ///         reproduce it, and pretending otherwise is what the falsified argument above did. It is
    ///         deliberately left to disagree, where the length belt converts it into backs for the
    ///         second or two it lasts.</item>
    /// </list>
    /// Splitting the two is the whole design: the derivable term is made exact so the belt is quiet in
    /// the ordinary case, and the underivable term is made VISIBLE so the belt can catch it.</para>
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
            // NULLS ARE SKIPPED, AND THAT IS THE ARC'S INDEX SPACE — not a shortcut. A null
            // inventory entry gets NO CHIP on either machine: the owner's arc skips it
            // (Cards.ItemsPile.Populate) and so does this walk, so slab i is chip i on both sides.
            // RemoteUsableFrame.ResolveSlots states the same rule from the other end and exists
            // ENTIRELY to translate record 35's RAW mask index into this COMPACTED arc index —
            // read its doc before touching this loop, because "AllItems is unfiltered" is true of
            // the LIST and says nothing about the ARC built from it. (I made exactly that mistake
            // one commit ago and this comment is the cheap way to stop the next reader repeating
            // it: the belt in Tick would then have refused every item front for good.)
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
        // …AND THEN THE OWNER'S OWN MEMBERSHIP FILTER, THE SAME EXPRESSION THEY APPLY. Without this
        // the mirrored arc counted widgets the owner's arc never seated (a long-rest placeholder, a
        // widget whose model card has gone), which put every slab behind such an entry one seat out
        // for as long as the card sat in the pile — a PERMANENT misalignment, not the transient one
        // the belt is for. Removing entries here in place keeps the surviving order intact, which is
        // the only property the positional zip needs.
        int kept = 0;
        for (int i = 0; i < _abilityBuf.Count; i++)
        {
            if (CardsGameApi.PileWidgetIsArcMember(_abilityBuf[i]))
                _abilityBuf[kept++] = _abilityBuf[i];
        }
        _abilityBuf.RemoveRange(kept, _abilityBuf.Count - kept);
        return _abilityBuf.Count > 0;
    }

    /// <summary>
    /// Fill <paramref name="into"/> with one flag per equipped item of the character
    /// <paramref name="owner"/>'s board is displaying: true where <c>CItem.SlotState</c> is
    /// <c>Spent</c>. Returns false (and leaves the list empty) when the peer's inventory cannot be
    /// read at all, which lands the caller on "nothing is tapped" — the safe direction, because it
    /// is the state a fresh arc is in.
    ///
    /// <para>WHY IT LIVES HERE RATHER THAN IN <see cref="RemoteItemFan"/>. This is the SAME walk of
    /// the SAME host-replicated list, in the same order, that <see cref="Resolve"/> does for the
    /// faces (<c>CPlayerActor.Inventory.AllItems</c>, resolved through the same
    /// <see cref="RemoteBoardFocus.DisplayedActor"/>). Slab <c>i</c> takes model entry <c>i</c> on
    /// both paths or the tap lands on the wrong chip, so the two must be one piece of code — the
    /// slot-alignment argument in <see cref="Resolve"/>'s doc applies here word for word.</para>
    ///
    /// <para>THAT CLAIM WAS UNTRUE WHEN IT WAS WRITTEN, and it is worth saying so here rather than
    /// letting the next reader trust it twice. This walk used the RAW <c>AllItems</c> index (a flag
    /// per entry, nulls included) while <see cref="Resolve"/> — and the owner's own arc, and every
    /// other consumer — SKIP the nulls. One null anywhere but the end of the list therefore tapped
    /// the chip BELOW the one the owner has spent. Both walk the compacted arc index now.</para>
    ///
    /// <para>WHICH SPACE IS THE RIGHT ONE IS NOT A JUDGEMENT CALL, and it is settled one file over:
    /// no chip is built for a null entry on EITHER machine (<c>Cards.ItemsPile.Populate</c> on the
    /// owner's side, <see cref="Resolve"/> on this one), which is why
    /// <see cref="RemoteUsableFrame.ResolveSlots"/> exists at all — its whole job is to translate
    /// record 35's mask out of the raw index and into this compacted one. "AllItems is unfiltered"
    /// is a true statement about the LIST that says nothing about the ARC.</para>
    ///
    /// <para>DELIBERATELY NOT BEHIND <see cref="RevealGate"/>, and this is the one place to say why.
    /// The gate governs card FRONTS: it exists so the game's secret
    /// <c>SelectAbilityCardsOrLongRest</c> window is not defeated by a peer's board. An equipped
    /// item's slot state is not an ability card and not a secret — the owner's own arc lies tapped in
    /// every phase, the flat game shows a party member's inventory outside VR, and what crosses to
    /// this client here is a BOOLEAN GEOMETRY FLAG, never an item id or a face. Gating it would make
    /// the mirrored arc disagree with its owner for exactly the phase the 1:1 ruling grants no
    /// exception to. Nothing is written; every read is null-guarded.</para>
    /// </summary>
    internal static bool TryResolveItemSpentFlags(RemoteAvatar owner, List<bool> into)
        => TryResolveItemArc(owner, into, null, out _);

    /// <summary>
    /// THE ONE WALK OF A PEER'S EQUIPPED ITEMS, and the ONE definition of the arc's index space.
    /// Fills <paramref name="spentInto"/> with one SPENT flag per arc slab (see
    /// <see cref="TryResolveItemSpentFlags"/> above for that half in full) and/or
    /// <paramref name="rawInto"/> with the RAW <c>AllItems</c> index each arc slab was built from,
    /// and reports the raw list's own length in <paramref name="rawLength"/>. Either output may be
    /// null; both are cleared first.
    ///
    /// <para>WHY THE RAW INDEX IS HANDED OUT AT ALL: two facts about a peer's items arrive in the
    /// RAW index space — record 35's usable mask and record 36's held-item seat — while the arc,
    /// on both machines, is the COMPACTED one. Every consumer therefore needs the same
    /// raw-to-arc translation, and the doc above records what happened the last time two of them
    /// spelled that rule out separately: the tap landed on the chip below the right one for as
    /// long as any null sat before it. <paramref name="rawInto"/><c>[arc]</c> is that mapping,
    /// inverted by the caller with a scan over a list bounded by
    /// <c>ItemsPile.UsableMaskBits</c>.</para>
    ///
    /// <para><paramref name="rawLength"/> is the belt, not decoration: a sender's seat is only a
    /// name for a card while both machines' copies of the list have the same length, which is the
    /// identical argument record 36's own length byte is built on
    /// (<see cref="RemoteHeldCardFace"/>). A caller that skips it can hide the wrong chip.</para>
    /// </summary>
    internal static bool TryResolveItemArc(RemoteAvatar owner, List<bool>? spentInto,
                                          List<int>? rawInto, out int rawLength)
    {
        spentInto?.Clear();
        rawInto?.Clear();
        rawLength = 0;
        if (owner == null)
            return false;
        try
        {
            CPlayerActor? actor = RemoteBoardFocus.DisplayedActor(owner, out _);
            CInventory? inv = actor != null ? actor.Inventory : null;
            List<CItem>? all = inv != null ? inv.AllItems : null;
            if (all == null)
                return false;
            rawLength = all.Count;
            // ONE FLAG PER CHIP, NOT ONE PER INVENTORY ENTRY — nulls skipped, exactly as Resolve
            // above and Cards.ItemsPile.Populate skip them. This walked the RAW index while every
            // arc on both machines is the compacted one, so a single null anywhere but the end of
            // AllItems tapped the chip BELOW the one the owner has actually spent. See the doc
            // above for why that could stand for so long, and RemoteUsableFrame.ResolveSlots for
            // the same translation done deliberately for record 35's mask.
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null)
                    continue;
                spentInto?.Add(all[i].SlotState == CItem.EItemSlotState.Spent);
                rawInto?.Add(i);
            }
            return true;
        }
        catch (System.Exception ex)
        {
            spentInto?.Clear();
            rawInto?.Clear();
            rawLength = 0;
            VRLog.Debug("Net", $"Remote item fan inventory read failed ({ex.Message}) — no chip " +
                               "is drawn tapped, and none is taken out of the arc, this cadence.");
            return false;
        }
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
        if (gate == Gate.CountMismatch)
        {
            // HW-VERIFY: this line decides multiplayer report item 5c. While it fires, the peer's
            // pile arc is showing BACKS because a front would have been the wrong card's — it is the
            // belt working, not a failure. It has to be readable in a SHIPPED log (Note), because
            // the only way to tell "the belt saved him" from "the belt never armed" is to see it
            // fire the moment a co-player burns a card. If it fires CONTINUOUSLY rather than for a
            // second or two around a burn, the two sides disagree in the steady state and the
            // shared filter below it has drifted from the owner's — that is the next lead.
            VRLog.Note("Net", line + ". The arc's slab count comes off the wire and is the size of " +
                              "the arc its OWNER is reading; the faces come from this client's own " +
                              "walk of that character's host-replicated pile. The two disagreeing " +
                              "means a card has moved on the owner's side that this client has not " +
                              "placed yet (a burn still holding its artwork, a card still flying " +
                              "into the stack, a played card still lying on their board), and the " +
                              "burnt pile is a CONCATENATION of the lost and permanently-lost lists " +
                              "— so the missing entry is in the MIDDLE and every slab behind it " +
                              "would draw its neighbour's card. Backs are the safe direction: a back " +
                              "reads as 'not loaded yet', a wrong front is believed and acted on.");
            return;
        }

        VRLog.Info("Net", line + ". Card/item identities are read LOCALLY from the peer's " +
                          "host-replicated model (CCharacterClass piles / Inventory.AllItems) and never " +
                          "from a packet — the reveal rule is a PHASE, not a place (user ruling " +
                          "2026-08-08): backs everywhere during SelectAbilityCardsOrLongRest, fronts " +
                          "everywhere otherwise. 'FRONTS' with fewer slabs than cards means a widget " +
                          "could not be resolved for the rest and they stay backs — the safe direction.");
    }
}
