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
    /// Index-aligned with <see cref="_slabs"/>.
    ///
    /// <para>IT IS MEASURED AT <see cref="Rebuild"/>, NOT ASSUMED, and the assumption it replaces was
    /// already false (2026-09-07 review R3 finding 6). This field used to be "seeded true because
    /// that is what <see cref="RemoteBrowserFan"/> and <see cref="RemoteItemFan"/> build their slabs
    /// with." <c>RemoteBrowserFan</c> does destroy all of its slabs, so the premise held there.
    /// <c>RemoteItemFan</c> does not: it deliberately lifts the detached clip SURVIVOR out of its
    /// destroy sweep and re-seats the same <c>GameObject</c> at the arc position record 26 now names,
    /// so the fan can be handed a slab that is still WEARING the card EDGE from an earlier
    /// <c>SetFrontFace(i, showsBack: false)</c> — <c>RemoteCardArt.Destroy</c> undoes the mesh
    /// hosting, never that material write. The edge test in <see cref="SetFrontFace"/> would then
    /// make every later "show a back" a no-op for that seat, and the chip would keep a card-edge
    /// frame around a plain back on every <see cref="ShowBacksEverywhere"/> path — the inverse of
    /// the user-item-10 defect this field exists to serve. <see cref="Rebuild"/> now WRITES the back
    /// through <c>CardMesh.SetBodyFrontFace</c> instead of believing in it; that write is idempotent
    /// by construction (it compares against the authored material and writes nothing when it already
    /// matches), so a fresh slab costs one reference compare per body.</para></summary>
    private readonly List<bool> _wearsBack = new(16);

    /// <summary>Reused resolve buffers — the per-cadence resolve allocates nothing.</summary>
    private readonly List<AbilityCardUI> _abilityBuf = new(16);
    /// <summary>The ITEM resolve buffer: <c>Inventory.AllItems</c> with its null entries SKIPPED,
    /// which is the ARC's index space — no chip is built for a null on either machine. See
    /// <see cref="Resolve"/>, and <see cref="RemoteUsableFrame.ResolveSlots"/> for the translation
    /// record 35's raw mask index needs to reach it.</summary>
    private readonly List<CItem> _itemBuf = new(16);

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

        /// <summary>
        /// THE SECRET WINDOW IS OPEN AND THIS IS AN ABILITY-CARD PILE ARC, so the gate is asked PER
        /// CARD instead of per fan (2026-09-07 review item B2 for the burnt arc; 2026-09-07 evening
        /// report item 3 for the discard arc).
        ///
        /// <para>THE NAME IS HISTORICAL AND THE MEMBER IS WIDER THAN IT. It started as the burn
        /// exception and now carries the whole pile-fan ruling — "Die Fächer der piles werden also
        /// ab jetzt immer mit Vorderseiten gezeigt ohne Ausnahme" — for BOTH ability-card arcs. A
        /// burnt card is opened by <c>RevealGate.IsPubliclyRevealedCard</c>, a discarded one by
        /// <c>RevealGate.IsDiscardedCard</c>, and both are asked through the same card-aware
        /// <c>RevealGate.CardFaces</c> overload, so this surface still states no rule of its own.
        /// The ITEM arc never reaches this member at all: an item has no <c>CardInstanceID</c>, so
        /// it is opened one level up by its POPULATION
        /// (<c>RevealGate.PeerCardPopulation.ItemCard</c>) and its gate is never
        /// <see cref="SecretPhase"/> to begin with.</para>
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
        /// <para>WHAT THIS PARAGRAPH USED TO SAY, KEPT SO THE REVERSAL IS LEGIBLE: "ONLY THE BURNT
        /// ARM, AND ONLY THIS ONE REFUSAL. <see cref="Content.Discard"/> and
        /// <see cref="Content.Items"/> keep the whole-fan gate — a discarded card is not burnt and
        /// its identity is still the selection window's secret." That was correct while nobody had
        /// ruled; the user has now ruled twice, and the ModBuild 478 logs measure the picture he is
        /// describing on both machines (<c>pile browse[p2] 0 FRONT / 4 BACK — Discard:
        /// RevealGate.ShowRoundCardFronts(actor)=false</c>). Only
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

        /// <summary>
        /// THE MAINTAINER'S 2026-09-07 (late) RULING: a peer's DISCARD arc is covered while that
        /// peer has a SHORT REST mid-choice. He was shown his own two same-day rulings colliding on
        /// one picture — the pile ruling drawing the SACRIFICED card face-up in a mirrored discard
        /// fan beside a decision row that already read "&lt;versiegelte Karte&gt;" — and chose to
        /// COVER THE PILE FAN DURING A SHORT REST, which restores his older and repeated "Kurze Rast
        /// = Auswahlphase = verdeckt".
        ///
        /// <para>IT IS ONE CASE AND MAY NOT BE WIDENED. The BURNT arc is never this member: "Beim
        /// Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer mit der Vorderseite sichtbar sein"
        /// is older, unconditional and more specific, so it wins wherever the two touch and a short
        /// rest does not close it. The ITEM arc cannot reach the secret-phase branch at all. Outside
        /// a short rest — the rest of the selection window included — the discard arc is
        /// <see cref="BurnException"/> and OPEN, exactly as ModBuild 479 left it.</para>
        ///
        /// <para>SEPARATE FROM <see cref="SecretPhase"/> ON PURPOSE, even though both draw backs. A
        /// census row has to be able to say WHICH rule covered the fan: this member is a RULING
        /// being honoured, <see cref="SecretPhase"/> is the phase gate. One shared reason string
        /// would make the two indistinguishable in the only instrument that measures the picture.
        /// </para>
        /// </summary>
        ShortRestCovered,
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
        Gate.BurnException => "RevealGate.ShowRoundCardFronts(actor)=false, but this is a PILE arc, " +
                              "so the gate is asked PER CARD through the card-aware " +
                              "RevealGate.CardFaces overload: every card in a burnt arc is in that " +
                              "character's Lost/PermanentlyLost lists and every card in a discard " +
                              "arc is in their DiscardedAbilityCards, both by construction, so " +
                              "RevealGate.IsPubliclyRevealedCard / IsDiscardedCard opens it — user, " +
                              "verbatim: 'Beim Verbrennen EGAL AUS WELCHEM GRUND muss die Karte " +
                              "immer mit der Vorderseite sichtbar sein' and 'Die Faecher der piles " +
                              "werden also ab jetzt immer mit Vorderseiten gezeigt ohne Ausnahme'. " +
                              "A BACK on this line is a card the ruling refused, which for either " +
                              "arc is a finding",
        Gate.ShortRestCovered => "the owner has a SHORT REST mid-choice — extension record 46 confirms " +
                                 "the choice is still in progress, independently of the sacrifice seat " +
                                 "or widget availability — so the DISCARD fan is covered, user " +
                                 "ruling 2026-09-07 (late): 'Kurze Rast = Auswahlphase = verdeckt', " +
                                 "given to settle his own pile ruling against the sealed decision " +
                                 "row. The BURNT fan is NOT covered by this and never is ('Beim " +
                                 "Verbrennen EGAL AUS WELCHEM GRUND'), the ITEM fan cannot reach " +
                                 "it, and outside a short rest the discard fan stays open",
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
            // MEASURE, DO NOT ASSUME — see _wearsBack's doc. A slab arriving here can be a SURVIVOR
            // RemoteItemFan carried across its own destroy sweep, still wearing the card EDGE from a
            // front this driver put up before the rebuild. Writing the back makes the belief below
            // true rather than hoping it is; the write is idempotent, so a freshly built slab pays
            // one reference compare per body and nothing else.
            CardMesh.SetBodyFrontFace(slab.transform, showsBack: true);
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
            //
            // WHICH POPULATION — and the ITEM arc names its own (user, 2026-09-07 evening item 3:
            // "Die Fächer der piles werden also ab jetzt immer mit Vorderseiten gezeigt ohne
            // Ausnahme", which he extended to the items fan in that message). An item is not an
            // ability card: it carries no CardInstanceID, so it can never be answered by the
            // per-card ruling the two ability arcs take below, and the population is the only
            // vocabulary both item surfaces share. RevealGate.PeerCardPopulation.ItemCard carries
            // the derivation, including why a KIND may sit on the exempt side of
            // RevealGate.IsPublicPopulation where a PLACE may not.
            RevealGate.CardFaceSource source =
                RevealGate.CardFaces(content == Content.Items
                                         ? RevealGate.PeerCardPopulation.ItemCard
                                         : RevealGate.PeerCardPopulation.Selectable, actor);
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

        // ─── THE SHORT REST COVERS THE DISCARD ARC, AND ONLY THAT ─────────────────────────────
        //
        // THE MAINTAINER RESOLVED THE COLLISION HIMSELF, 2026-09-07 (late), and this branch is his
        // answer. He was shown his own two same-day rulings disagreeing on one picture: during a
        // short rest a peer's mirrored DISCARD fan drew the SACRIFICED card face-up while the
        // decision row beside it read "<versiegelte Karte>", because Net.DecisionLabelMask masks the
        // NAME (ModBuild 477 item 7) while the pile ruling opened the FACE. HE CHOSE: COVER THE PILE
        // FAN DURING A SHORT REST. That narrows "Die Fächer der piles werden also ab jetzt immer mit
        // Vorderseiten gezeigt ohne Ausnahme" (2026-09-07 evening, item 3) by exactly one case, and
        // lines it up with his older, repeated ruling: "Kurze Rast = Auswahlphase = verdeckt".
        //
        // THE SCOPE IS ONE CASE AND IS NOT TO BE WIDENED. Discard fan DURING A SHORT REST: covered.
        // BURNT fan: open, always, no exception, in every phase — "Beim Verbrennen EGAL AUS WELCHEM
        // GRUND muss die Karte immer mit der Vorderseite sichtbar sein" is older, unconditional and
        // more specific, so it wins wherever the two touch and a short rest does NOT close it. ITEMS
        // fan: untouched (its population is already exempt above and cannot arrive here). Every fan
        // OUTSIDE a short rest, the rest of the selection window included: unchanged, open.
        //
        // Record 46 carries the owner's choice state independently of the card-seat record.
        // Inferring this from record 39 failed open whenever the widget was not resolvable or
        // the sacrifice was still flying into its recess. The committed rest is all the game's
        // ProxyShortRest replicates; the receiver cannot reconstruct the deliberation window.
        // Record 39 still removes the physically seated card from the arc, but no longer decides
        // whether that arc is secret. An omitted record 46 resets the state on the next packet.
        //
        // NOT A CAPABILITY FAILURE. Only Gate.SecretPhase is touched; NoActor, OffScenario and
        // Errored are capability failures, not secrecy verdicts, and no ruling about what may be
        // shown can argue a capability failure into a face.
        if (gate == Gate.SecretPhase && actor != null)
        {
            if (content == Content.Discard && _owner.ShortRestInProgress)
            {
                gate = Gate.ShortRestCovered;
                LogShortRestCover(actor);
            }
            // MB487: every selection-phase pile/item front remains covered. Membership in
            // a spent or active list no longer turns this into a per-card exemption pass.
        }
        if (gate != Gate.ShortRestCovered)
            _loggedShortRestCover = int.MinValue; // re-arm, so the NEXT short rest announces itself

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

        // Resolve the current membership and state every frame. Equal counts do not identify
        // the same inventory/pile, and no owner animation owes a 250 ms observer-only delay.
        // Front hosts and mip maintenance keep their own identity/dirty gates below.
        // A pile switch on the same slabs must not carry the old pile's dedup keys across.
        if (contentKey != _resolvedContent && _resolvedContent >= 0)
        {
            for (int i = 0; i < _arts.Count; i++)
                _arts[i].HideFront();
            ShowBacksEverywhere();
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
        if (resolved && _droppedSeats > 0)
            LogDroppedSeats(content, _droppedSeats, actor);

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
        // This is a BELT, deliberately kept even after Resolve was taught the owner's own filter.
        //
        // WHAT THE RESIDUE IS NOW, AND IT IS SMALLER THAN THE SENTENCE THAT USED TO STAND HERE. That
        // sentence read: "the residual divergence (a card whose visual is still on the owner's board
        // or in flight) is sender-local VR state that has no representation on this client at all,
        // so no amount of model reading can reproduce it." The second half is still true of a card
        // IN FLIGHT. The first half was FALSE of a card LYING ON THE BOARD, and being false made
        // this belt stand in for a rule for two builds: the owner's BoardOwnsCardVisual drops
        // _shortRestCard and _fieldCards from their arc while the rules model still lists both in
        // DiscardedAbilityCards, the lengths disagreed for the whole decision, and every mirrored
        // discard fan went to backs. For the SHORT REST that picture is what the maintainer wants,
        // but it is not this belt's business to deliver it — Gate.ShortRestCovered above is the rule
        // now. For the LONG REST's burn pick it was simply wrong: a long rest is the ACTION phase
        // and is FULLY OPEN, so that fan owes FRONTS. Both cards ARE represented on this client —
        // extension record 39 carries the seat — and Resolve.DropBoardHeldSeats takes them out, so
        // the two counts agree and the long-rest fan draws its fronts.
        //
        // WHAT IS LEFT FOR THE BELT is the ARRIVAL term alone — a card mid-flight into the stack,
        // one still held by its burn artwork, one the live pick FAN has borrowed. None of those is
        // lying in a recess, so record 39 says nothing about them by construction, and they are
        // precisely the 2026-09-02 item 5c case above: the co-player burns a card, it is appended to
        // the MIDDLE of the burnt concatenation, and the owner's arc has not seated it yet. That
        // window is NetProtocol.CardFxSeconds of flight plus the burn hold, and any viewer with that
        // peer's burnt fan open while they burn reaches it in one action — this belt is live code
        // and not a relic. It is what turns that residue from a wrong front into a back.
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
                        // The DISPLAYED actor, resolved once at the top of this Rebuild, is passed
                        // so the durable look can ask the burnt LISTS and not only the card's
                        // CurrentCardPile stamp: a card burnt to negate damage never gets that stamp
                        // written (CCharacterClass.MoveAbilityCard), so without it the burnt fan
                        // draws that card pristine while its owner sees it charred.
                        RemoteCardArt.CardFxLook want =
                            UsedCardLook.FromState(actor, full != null ? full.AbilityCard : null);
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
            LogBurnCarveRefusal(content, carvedBacks, fronts, actor);
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
    /// <para>The carve-out rests on ONE claim per arc, and both are membership by construction:
    /// every card in a peer's BURNT browse arc is in that character's <c>LostAbilityCards</c> or
    /// <c>PermanentlyLostAbilityCards</c> (what <c>RevealGate.IsPubliclyRevealedCard</c> tests), and
    /// every card in their DISCARD browse arc is in <c>DiscardedAbilityCards</c> (what
    /// <c>RevealGate.IsDiscardedCard</c> tests) — both arcs are built out of exactly those lists by
    /// <c>CardsGameApi.GetPileWidgets</c>. So the per-card ask can only ever say yes. This line
    /// fires exactly when it said no — i.e. when the claim is false — and it is the only way to find
    /// that out, because a refused slab draws the SAME card back the old whole-fan gate drew and is
    /// invisible in every other reading.</para>
    ///
    /// <para>SILENCE IS THE EXPECTED READING, and it is not a "held instrument": the enclosing arm
    /// only runs while a viewer has a peer's BURNT or DISCARD pile open DURING that peer's secret
    /// selection window, so nothing at all is owed outside that intersection. The reading that means
    /// the carve-out is live and working is <c>Gate.BurnException</c> appearing in this surface's
    /// census line with a non-zero front count.</para>
    ///
    /// <para>ONE REFUSAL IS EXPECTED AND IS NOT A DEFECT, AND IT IS NAMED HERE SO IT IS NOT CHASED:
    /// a card the owner is holding out of the arc is not DRAWN, so it never reaches the per-card ask
    /// and cannot be counted here. A refusal on this line is always a card that IS on screen.</para>
    /// </summary>
    private void LogBurnCarveRefusal(Content content, int refused, int fronts, CPlayerActor? actor)
    {
        int key = ((int)content << 16) | (refused << 8) | (fronts & 0xFF);
        int actorId = NetFigures.StableActorId(actor);
        if (key == _loggedCarveRefusal.Key && actorId == _loggedCarveRefusal.ActorId)
            return;
        _loggedCarveRefusal = (key, actorId);
        // HW-VERIFY: grep token "BURNT ARC CARVE-OUT REFUSED" — see this method's doc.
        VRLog.Note("Net", $"BURNT ARC CARVE-OUT REFUSED [player {_owner.PlayerId}]: {refused} slab(s) "
                          + $"of this {content.ToString().ToUpperInvariant()} arc stayed BACKS beside "
                          + $"{fronts} front(s) for "
                          + $"'{Board.CharacterFocus.Describe(actor)}', inside that peer's secret "
                          + "selection window. The premise of the carve-out is that every card in a "
                          + "burnt browse arc is in that character's Lost/PermanentlyLost lists and "
                          + "every card in a discard browse arc is in their DiscardedAbilityCards, "
                          + "both by construction, so RevealGate.IsPubliclyRevealedCard / "
                          + "IsDiscardedCard cannot refuse one. "
                          + "THIS LINE IS THAT PREMISE BEING FALSE. The two candidates are a widget "
                          + "with no model CAbilityCard behind it (CardsGameApi.PileWidgetIsArcMember "
                          + "should already have dropped it) and a length disagreement that slipped "
                          + "past the belt above — read the fan's own count line beside this one. "
                          + "The user's rulings are 'Beim Verbrennen EGAL AUS WELCHEM GRUND' and "
                          + "'Die Faecher der piles werden also ab jetzt immer mit Vorderseiten "
                          + "gezeigt ohne Ausnahme', so any occurrence is a finding and not a "
                          + "footnote.");
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

    /// <summary>Local model identities for preserving resident browse-card poses across a reflow.</summary>
    internal void CopyResolvedAbilityIds(List<int> into)
    {
        into.Clear();
        for (int i = 0; i < _abilityBuf.Count; i++)
        {
            CAbilityCard? card = _abilityBuf[i]?.AbilityCard;
            if (card == null) { into.Clear(); return; }
            into.Add(card.CardInstanceID);
        }
    }

    internal void ResolveAbilityIds(Content content, List<int> into)
    {
        into.Clear();
        if (content == Content.Items) return;
        CPlayerActor? actor = RemoteBoardFocus.DisplayedActor(_owner, out _);
        if (actor == null) return;
        try
        {
            if (Resolve(actor, content)) CopyResolvedAbilityIds(into);
        }
        catch (System.Exception) { into.Clear(); }
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
        _droppedSeats = 0; // the item arm never sets it, and a stale count would be logged as new

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
        // THE OWNER'S OWN ARC, AS ONE CALL. This is CardsGameApi.GetPileWidgets narrowed by
        // CardsGameApi.PileWidgetIsArcMember — the owner's own membership filter, the same
        // expression they apply (CardsDriver.UpdateBrowser), and it used to be spelled out inline
        // here. It is the NAMED combination now for one reason that matters below: it is the index
        // space extension record 39 seats a card in, and an index is only a name for a card while
        // both machines build the list with the SAME expression. Without the membership term at all
        // the mirrored arc counted widgets the owner's arc never seated (a long-rest placeholder, a
        // widget whose model card has gone), which put every slab behind such an entry one seat out
        // for as long as the card sat in the pile — a PERMANENT misalignment, not the transient one
        // the belt is for.
        CardsGameApi.GetPileArcWidgets(hand, content == Content.Burnt, _abilityBuf);
        DropBoardHeldSeats(content);
        return _abilityBuf.Count > 0;
    }

    // Record 46 is the flow's explicit state. Inferring it from record 39 failed open when
    // the sacrifice could not be seated and conflated short rests with modal discard picks.

    /// <summary>The actor the short-rest cover line last named (<c>int.MinValue</c> = not covered
    /// right now), so the line fires on the EDGE into a peer's short rest and not on the per-frame
    /// gate path — and fires again for their next rest, because <see cref="Tick"/> clears it the
    /// moment the cover lifts.</summary>
    private int _loggedShortRestCover = int.MinValue;

    /// <summary>
    /// HARDWARE VERIFICATION for the ruling above — grep token <c>SHORT REST PILE COVER</c>.
    ///
    /// <para>WORKING = this line firing while a co-player has a short rest mid-choice and raises his
    /// DISCARD fan, with the observer's <c>pile browse[pN]</c> census row reading
    /// <c>0 FRONT / N BACK</c> and the <c>Gate.ShortRestCovered</c> rule string. The fan is covered
    /// BY THE RULE and not by a count disagreement — a <c>Gate.CountMismatch</c> row in that moment
    /// would mean the rule did not fire and the belt did.</para>
    ///
    /// <para>INERT, AND THIS IS THE READING THAT MATTERS = a co-player takes a short rest, opens his
    /// discard fan, and the census row reads FRONTS with the <c>Gate.BurnException</c> rule string
    /// and NO line here. Then extension record 39 named no discard seat, and the rule cannot see the
    /// rest at all — a fail-OPEN on a secrecy rule. The deciding field is on the OWNER's machine and
    /// already shipped: <c>LocalRigSampler.SampleSacrificeSeats</c>'s <c>SHORT REST SEAT</c> line
    /// states which of its three refusals fired (the sacrificing hand is not the presented character
    /// / no widget resolved / the seat does not fit in 5 bits). There is no second source to fall
    /// back on: <c>CardsHandUI.PerformShortRest</c> runs on the owner's machine alone and FFSNet's
    /// <c>CardsHandUI.ProxyShortRest</c> replays only the COMMITTED rest (it calls
    /// <c>FinalizeShortRest</c>), so this client's copy of that hand has <c>_shortRestedCard</c>
    /// null for the whole deliberation.</para>
    ///
    /// <para>BEYOND = this line firing for a BURNT arc. It cannot: the caller asks only on
    /// <see cref="Content.Discard"/> and this method requires the DISCARD list id. If it ever does,
    /// the burn ruling has been widened by accident and that is a finding on its own.</para>
    /// </summary>
    private void LogShortRestCover(CPlayerActor? actor)
    {
        int actorId = NetFigures.StableActorId(actor);
        if (actorId == _loggedShortRestCover)
            return;
        _loggedShortRestCover = actorId;
        // HW-VERIFY: grep token "SHORT REST PILE COVER" — see this method's doc for the readings.
        VRLog.Note("Net", $"SHORT REST PILE COVER [player {_owner.PlayerId}]: the DISCARD fan of "
                          + $"'{Board.CharacterFocus.Describe(actor)}' is COVERED — extension record "
                          + "46 explicitly confirms the short-rest choice is in progress, independently "
                          + "of record 39's seat resolution and widget availability. User ruling 2026-09-07 "
                          + "(late), given to settle his own pile ruling against the sealed decision "
                          + "row: 'Kurze Rast = Auswahlphase = verdeckt'. It used to be covered by "
                          + "the LENGTH BELT instead — the owner's arc drops the sacrifice while the "
                          + "rules model still lists it — which is the right picture from the wrong "
                          + "mechanism, and it stopped holding the moment Resolve.DropBoardHeldSeats "
                          + "gave the two counts a reason to agree. This line is the rule. The BURNT "
                          + "fan is untouched and stays open ('Beim Verbrennen EGAL AUS WELCHEM "
                          + "GRUND'), and so does every discard fan outside a short rest.");
    }

    /// <summary>How many arc seats the last <see cref="Resolve"/> dropped because the OWNER told us
    /// their card is lying on their board (extension record 39). Read by <see cref="Log"/> so a belt
    /// trip can be told apart from a belt trip THIS term did not manage to prevent.</summary>
    private int _droppedSeats;

    /// <summary>The (content, dropped, arc length) triple the drop line last stated, so it fires on
    /// a real edge and not on the 4 Hz resolve cadence.</summary>
    private (int Key, int ActorId) _loggedDroppedSeats = (int.MinValue, 0);

    /// <summary>
    /// TAKE OUT OF THE MIRRORED ARC THE CARDS THE OWNER'S ARC LEFT ON THEIR BOARD — the residue the
    /// length belt was catching, closed by being TOLD rather than by being guessed at.
    ///
    /// <para>WHAT WAS MEASURED (2026-09-07 review R1 finding 1, re-derived in code). The wire's slab
    /// count is <c>PileBrowser.Cards.Count</c>, i.e. the owner's <c>_browseBuffer</c>, and
    /// <c>CardsDriver.UpdateBrowser</c> drops from it every card for which
    /// <c>BoardOwnsCardVisual</c> is true — a predicate that names <c>_shortRestCard</c> and
    /// <c>_fieldCards</c> by name. Meanwhile <c>CardsHandUI.PerformShortRest</c> picks the sacrifice
    /// out of <c>DiscardedAbilityCards</c> and REMOVES NOTHING, so the walk above still counts it.
    /// Owner sends N−1, this client resolves N, the belt trips and the peer's whole pile fan goes
    /// to BACKS.</para>
    ///
    /// <para>THE FLOW THIS IS FOR IS THE LONG REST, AND ONLY IT. The maintainer ruled on
    /// 2026-09-07 (late) that a peer's discard fan IS covered during a SHORT REST, so for that flow
    /// the backs are the wanted picture — but delivered by <see cref="Gate.ShortRestCovered"/>, a
    /// rule, and not by two counts disagreeing. A LONG REST is the ACTION phase and is FULLY OPEN:
    /// its burn step lays the chosen card into a recess (<c>_fieldCards</c>), the same arithmetic
    /// fired, and every mirrored discard fan went to backs in a phase whose ruling grants no
    /// exception. That is the defect this method closes. The belt is right and stays; what was wrong
    /// is that the DERIVABLE half of the owner's filter stopped one term short.</para>
    ///
    /// <para>THE OWNER ALREADY NAMES THESE CARDS, AND IN THIS EXACT INDEX SPACE. Extension record 39
    /// carries, per round recess, a <c>[list id + seat][list length]</c> for the card the owner has
    /// physically lying in that recess — the short-rest sacrifice
    /// (<c>CardsDriver.SacrificeSeat</c>) and a modal pick's card
    /// (<c>CardsDriver.PickFieldSeat</c>, which reads <c>_fieldCards</c>), both seated by
    /// <c>LocalRigSampler.SampleSacrificeSeats</c> through
    /// <c>CardsGameApi.GetPileArcWidgets</c> — the same call <see cref="Resolve"/> makes one line
    /// up. So this is not a re-derivation of a sender-local fact: it is the sender's own statement,
    /// decoded in the receiver's own list. NO WIRE CHANGE, no new byte, no new record.</para>
    ///
    /// <para>WHY IT CANNOT SILENTLY MISALIGN, which is the only way a change here could be worse
    /// than the defect. Three properties, and they are what make this "by construction":
    /// <list type="bullet">
    ///   <item>The seat is a SUBSET of the owner's own drop set. Record 39 is written only while
    ///         <c>SacrificeSeat</c>/<c>PickFieldSeat</c> report a card PHYSICALLY in a recess, and
    ///         both of those cards are in <c>BoardOwnsCardVisual</c>. So a seat named here was
    ///         certainly dropped there; the reverse does not hold and does not need to.</item>
    ///   <item>The LENGTH BELT of the record itself is re-applied here, exactly as
    ///         <c>RemoteControlBoard.TryResolveSacrifice</c> applies it: unless this client's arc is
    ///         the same length the sender seated against, the seat has shifted under us and NOTHING
    ///         is dropped. The fan then falls through to <see cref="Tick"/>'s belt and shows backs,
    ///         which is where it was before this method existed.</item>
    ///   <item>The LIST ID has to match the arc being resolved, so a burn pick's card cannot be
    ///         taken out of the discard arc. <c>NetProtocol.RecessSeatListAllowed</c> refuses
    ///         <c>HeldFaceListHand</c> at the decode, so a card of the two-card commit is not
    ///         expressible here even in principle — the anti-cheat boundary is untouched, and
    ///         nothing about a FACE is decided in this method at all.</item>
    /// </list>
    /// Dropping FEWER than the owner leaves a length disagreement, which is backs. Dropping a
    /// DIFFERENT card is what the length belt above forbids. Both failure directions are the safe
    /// one.</para>
    ///
    /// <para>WHAT THE BELT IS STILL FOR, AND IT IS STILL REACHABLE. The residue this does NOT close
    /// is the ARRIVAL term — <c>CardEnRouteToPile</c> and <c>_fanBuffer</c> in the owner's filter: a
    /// card mid-flight into the stack, one still held by its burn artwork, one the live pick FAN has
    /// borrowed. None of those is lying in a recess, so record 39 says nothing about them by
    /// construction, and they are exactly the 2026-09-02 item 5c case the belt was built for (the
    /// co-player burnt a card and the peer saw a DIFFERENT card marked burnt). That flight is ~0.4 s
    /// on <c>NetProtocol.CardFxSeconds</c> and the burn hold is longer still, so a viewer with a
    /// peer's burnt fan open while that peer burns a card reaches it in one action.</para>
    /// </summary>
    private void DropBoardHeldSeats(Content content)
    {
        _droppedSeats = 0;
        byte wantList = content == Content.Burnt
            ? NetProtocol.HeldFaceListBurnt
            : NetProtocol.HeldFaceListDiscard;
        // Collected against the PRE-drop length, because that is the length the sender seated
        // against and the length its own belt compares — removing one entry would move the other.
        int seatA = -1;
        int seatB = -1;
        for (int slot = 0; slot < NetProtocol.BoardUiSlotCount; slot++)
        {
            byte code = _owner.SacrificeSeatCode(slot);
            if (!NetProtocol.HeldFaceNamesCard(code))
                continue;
            byte list = NetProtocol.HeldFaceList(code);
            if (!NetProtocol.RecessSeatListAllowed(list) || list != wantList)
                continue;
            if (_abilityBuf.Count != _owner.SacrificeSeatCount(slot))
                continue; // lengths already disagree — the seat has shifted; let the belt speak
            int at = NetProtocol.HeldFaceIndex(code);
            if (at >= _abilityBuf.Count || at == seatA || at == seatB)
                continue;
            if (seatA < 0)
                seatA = at;
            else
                seatB = at;
        }
        if (seatA < 0)
            return;
        // Descending, so the first removal cannot move the second's index.
        if (seatB > seatA)
            (seatA, seatB) = (seatB, seatA);
        _abilityBuf.RemoveAt(seatA);
        _droppedSeats++;
        if (seatB >= 0)
        {
            _abilityBuf.RemoveAt(seatB);
            _droppedSeats++;
        }
    }

    /// <summary>
    /// HARDWARE VERIFICATION for the drop above — grep token <c>PILE ARC BOARD-HELD SEAT</c>.
    ///
    /// <para>WORKING = this line firing with <c>dropped</c> ≥ 1 while a co-player has a LONG REST's
    /// burn card laid in a recess, and NO <c>Gate.CountMismatch</c> census row for this surface
    /// beside it — the peer's discard fan stays FRONTS through the whole pick, which is what the
    /// action phase owes.</para>
    ///
    /// <para>INERT = a <c>Gate.CountMismatch</c> row for a pile browse during a co-player's
    /// long-rest burn pick with NO line here. Then record 39 named nothing for that recess, and the
    /// reason is in the sender's own <c>SHORT REST SEAT</c> line (the record's log name predates its
    /// second case), which states which of its refusals fired. That is a record-39 defect, not this
    /// term's.</para>
    ///
    /// <para>BEYOND = this line firing with <c>dropped</c> ≥ 1 AND a <c>Gate.CountMismatch</c> row
    /// for the same surface at the same moment. The drop was applied and the lengths still disagree,
    /// which means a SECOND card is off the owner's arc — the arrival residue this deliberately does
    /// not close (see <see cref="DropBoardHeldSeats"/>), or a board zone
    /// <c>CardsDriver.BoardOwnsCardVisual</c> names that record 39 cannot.</para>
    /// </summary>
    private void LogDroppedSeats(Content content, int dropped, CPlayerActor? actor)
    {
        int key = ((int)content << 16) | ((dropped & 0xFF) << 8) | (_arts.Count & 0xFF);
        int actorId = NetFigures.StableActorId(actor);
        if (key == _loggedDroppedSeats.Key && actorId == _loggedDroppedSeats.ActorId)
            return;
        _loggedDroppedSeats = (key, actorId);
        // HW-VERIFY: grep token "PILE ARC BOARD-HELD SEAT" — see this method's doc for the three
        // readings.
        VRLog.Note("Net", $"PILE ARC BOARD-HELD SEAT [player {_owner.PlayerId}]: {dropped} seat(s) "
                          + $"taken out of this client's {content.ToString().ToUpperInvariant()} arc "
                          + $"for '{Board.CharacterFocus.Describe(actor)}' before the length belt, "
                          + $"leaving {_abilityBuf.Count} against {_arts.Count} slab(s) off the wire. "
                          + "The owner's own arc (CardsDriver.UpdateBrowser) drops any card whose VR "
                          + "visual their board is holding — BoardOwnsCardVisual names the short-rest "
                          + "sacrifice and the modal pick field by name — while the RULES MODEL still "
                          + "lists it (CardsHandUI.PerformShortRest removes nothing from "
                          + "DiscardedAbilityCards). Extension record 39 is the owner saying WHICH "
                          + "arc seat that is, in this same GetPileArcWidgets index space, so the two "
                          + "counts agree by construction instead of by luck. Nothing about a FACE is "
                          + "decided here and no card identity crossed the wire: what arrived is a "
                          + "seat number.");
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
                              "reads as 'not loaded yet', a wrong front is believed and acted on. " +
                              $"Extension record 39 took {_droppedSeats} board-held seat(s) out of " +
                              "this arc before the belt was asked (grep PILE ARC BOARD-HELD SEAT), " +
                              "so a trip WITH a non-zero count there is a SECOND card off the " +
                              "owner's arc and a trip with zero during a co-player's short rest or " +
                              "long-rest burn pick means record 39 named no seat — read the " +
                              "sender's own SHORT REST SEAT line, which states which refusal fired.");
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
