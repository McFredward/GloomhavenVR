using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// A PEER'S BURN, ON THE RIGHT CARD, AT THE RIGHT MOMENT — the receiving half of the burn flow
/// (2026-09-05 multiplayer report, items 11a and 11b).
///
/// <para>WHAT HE REPORTED, VERBATIM: "Immer noch sieht der Mitspieler eine andere Karte die
/// verbrannt wurde als ich. Und auch sieht er nicht die Feuer Animation die ich sehe. Verletzung
/// der 1:1 Regel."</para>
///
/// <para>THE CAUSE IS STRUCTURAL AND THE WIRE SAYS IT IN ITS OWN WORDS. Everything a peer was
/// given about a burn is <see cref="NetCardFx"/>'s two-byte event: a low nibble FROM anchor, a
/// high nibble TO anchor, and a wrapping sequence. <see cref="RemoteCardFx"/>'s own doc states the
/// consequence — "the slab is a BACK on both faces… No card identity is ever transmitted or
/// rendered here." So the peer flew a blank card back. He could not see WHICH card burned because
/// nothing named one, and he could not see the fire because a back slab has no face to burn. Both
/// halves of the report are the same missing fact.</para>
///
/// <para>AND THE STANDING ARGUMENT FOR WHY IT COULD NOT BE FIXED IS FALSE. <c>RemoteAvatar</c>'s
/// held-card note says of this very surface: "a flight is an EVENT, not a state, and the card it
/// carries is in transit BETWEEN two lists — the model has usually already moved it by the time
/// the receiver replays the arc, so there is no list position that names it for the duration of
/// the flight. It needs a different record." That is true of a DISCARD and false of a BURN, and
/// the difference is the whole of this class. A burned card is not in transit: the game commits it
/// into <c>CCharacterClass.LostAbilityCards</c> BEFORE it starts the card's burn artwork (that
/// ordering is the reason the owner's own <c>CardsDriver.TickBurnToPile</c> has to hold the card
/// on the board at all), and it stays in that list for the rest of the scenario. That list is
/// HOST-REPLICATED and this client already walks it — <see cref="RemotePileFronts.Resolve"/> reads
/// exactly it, through exactly the same <c>CardsGameApi.GetPileWidgets</c> call, to draw the
/// peer's burnt-pile fan. A card ARRIVING in it is a burn, and naming it costs nothing.</para>
///
/// <para>SO NO WIRE FIELD IS OWED, and adding one would have been the wrong fix twice over: it
/// would have put a card identity on the wire for the first time (the anti-cheat line every remote
/// card surface is built to hold) to transmit a fact the receiver already has. The identity is
/// resolved LOCALLY here, gated by the identical <see cref="RevealGate.CardFaces"/> call the board
/// slots, the hand fan, the pile fans and the held-card face all make.</para>
///
/// <para>THAT SENTENCE NAMED <c>RevealGate.ShowRoundCardFronts</c> UNTIL 2026-09-07 AND THIS FILE
/// HAS NEVER CALLED IT. The difference is not cosmetic and it is the whole of report item 6: the
/// bare phase predicate draws a BACK for the entire duration of a burn that happens inside its
/// owner's own selection window, which is what he reported. The card-aware overload is what carries
/// the burn exception, and a comment naming the narrower predicate is how a future reader
/// "simplifies" the wider one away.</para>
///
/// <para>THE CHOREOGRAPHY MIRRORS THE OWNER'S, TERM FOR TERM. The owner's card lies on his board
/// while the game's <c>BurnCardTimeline</c> plays on it (<c>burnTime = 2 s</c>) and only then
/// flies into his Burnt stack on a <see cref="VRCard.FlyToPile"/> arc. This plays the same two
/// phases against the SENDER'S OWN synced board pose: hold at their board anchor with the burn
/// ramping on the real face, then the same arc to their Burnt stack over
/// <see cref="NetProtocol.CardFxSeconds"/>. Both clocks start from the same host-replicated pile
/// change, so they are in step by construction rather than by a timestamp we would have had to
/// send.</para>
///
/// <para>THE WIRE EVENT IS NOT REMOVED — it is CONSUMED. While this mirror is presenting a burn for
/// this owner, <see cref="ConsumesWireEvent"/> swallows the matching <c>Board -&gt; Burnt</c>
/// event so there is never a second flight; when this mirror cannot present one (their board is
/// hidden, the reveal gate is shut, no hand on this client, the face would not clone) the event
/// passes through and the peer gets exactly the back slab he got before. Degrading to the old
/// picture is the failure mode, never nothing.</para>
///
/// <para>NOTHING HERE WRITES GAME STATE. Every list is walked read-only, the face is a throwaway
/// CLONE built by <see cref="RemoteCardArt"/>, the burn look is rebuilt on materials that overlay
/// mints and destroys (never the game's own <c>CardEffects</c>, which cannot run on a detached
/// clone and would write a pooled widget — see <c>RemoteCardArt.BuildBurnRig</c>), and the body
/// mesh comes out of <see cref="CardMesh"/>'s shared cache.</para>
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire. The burned card's identity is resolved
/// from the host-replicated <c>CCharacterClass.LostAbilityCards</c> list this client already reads
/// for the peer's burnt-pile fan, never from a packet, and every face is gated by
/// <see cref="RevealGate"/>. No new wire field, no new record, no byte added to any packet. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteBurnFx
{
    /// <summary>
    /// CEILING on how long a burned card may be held before it flies, in seconds — NOT a duration
    /// any more.
    ///
    /// <para>IT STOPPED BEING A DURATION (report item 9's third clause), THEN STOPPED BEING A
    /// CEILING (report item 5), AND THE THING THAT REPLACED IT WAS ALSO WRONG (report item 8). The
    /// hold ran for as long as the OWNER's card stayed seated in the recess this client mirrors —
    /// a proxy for his artwork that ModBuild 474 measured against his own hold on all three burns
    /// of the session and found early, late and early again (+0.33 / −0.48 / −1.45 s; the table is
    /// in <see cref="Drive"/>). The hold now runs on <see cref="BurnArtwork.Released"/>, the
    /// owner's own release expression over the owner's own widget, and the recess decides only who
    /// DRAWS the card while it lies there. This number is not consulted by the hold at all.</para>
    ///
    /// <para>WHAT IT STILL IS: the ORIGIN of phase 2's clock. <see cref="Drive"/> reads
    /// <c>(Elapsed - HoldSeconds) / CardFxSeconds</c> for the arc's parameter and
    /// <see cref="Handover"/> re-bases <c>Elapsed</c> to exactly this value when the hold ends, so
    /// every arc runs its full length however long the hold before it took. Any value would do for
    /// that arithmetic; it is kept AT the game's burn duration because that is what the number
    /// means everywhere else it is quoted.</para>
    ///
    /// <para>MIRRORED CONSTANT, and the thing it mirrors is the GAME's, not ours:
    /// <c>CardEffects.BurnCardTimeline</c> opens with <c>float burnTime = 2f</c>
    /// (CardEffects.cs:515) and that loop is the whole visible burn. The owner's own hold ends when
    /// that timeline's coroutine handle goes null (<c>CardsDriver.BurnArtworkActive</c>), so this
    /// is the same duration reached from the other side rather than a second dial. It is
    /// deliberately NOT <c>CardsDriver.BurnEffectMaxHoldSeconds</c> (3 s): that is the owner's
    /// belt for a burn whose artwork never starts, and copying a CEILING as if it were a duration
    /// is what made every burn on ModBuild 447 run the full 3 s.</para>
    /// </summary>
    private const float HoldSeconds = 2f;

    /// <summary>
    /// Seconds between two walks of the peer's host-replicated Lost pile — THE UPPER BOUND ON HOW
    /// LATE THIS MIRROR CAN LEARN OF A BURN, and therefore on report item 9's first clause ("Die
    /// Animation beim remote Board … ist immer noch etwas verzögert").
    ///
    /// <para>IT IS NOT <c>RemoteBoardContent.RefreshSeconds</c> ANY MORE, and the two really are
    /// different questions. That cadence paces a STATE mirror — occupancy, counts, faces — where a
    /// quarter-second of staleness is invisible because the picture it produces is the same picture
    /// a moment later. This is an EVENT edge, and the whole of it is WHEN: the ModBuild 462 host log
    /// carries five mirrored burns and every one of them reports 255-259 ms between the walk that
    /// found it and the walk before, i.e. the cadence itself was the entire measured delay on every
    /// burn in the session. Sharing a dial with a state mirror was the reason.</para>
    ///
    /// <para>WHAT IT COSTS. The walk is two list scans over lists this client already holds
    /// (<c>CardsGameApi.GetPileWidgets</c> → <c>AppendPileWidgets</c>, the character's Lost pile
    /// against <c>hand.cardsUI</c>) plus a hash-set diff — no <c>FindObjectsOfType</c>, no
    /// allocation, no component search. Three times the rate of a 4 Hz pass on a handful of peers is
    /// not measurable beside the board pass it used to ride on.</para>
    ///
    /// <para>IT DOES NOT SET THE VISIBLE START ANY MORE, WHICH IS WHY IT ONLY HAD TO BE SMALLER AND
    /// NOT ZERO. Since <see cref="Drive"/> hands over on the recess-emptying edge, a burn discovered
    /// well before the owner's card leaves its seat costs nothing at all; this number is the delay
    /// only for a burn that is discovered AFTER the seat has already gone — the short-rest case.</para>
    /// </summary>
    private const float WatchSeconds = 0.08f;

    /// <summary>Concurrent presentations cover the complete native card stream population. The
    /// old three-slab pool could recycle an unfinished burn when several active bonuses expired
    /// together. Legitimate simultaneous native cards must all retain their own presentation.</summary>
    private const int MaxBurns = CardAppearanceState.CountMax;

    /// <summary>Slack added to (hold + flight) before this mirror stops claiming the owner's
    /// <c>Board -&gt; Burnt</c> wire event. The owner reports the flight when HIS hold releases,
    /// which is up to his own 3 s ceiling after the pile change this mirror triggered on — so the
    /// window has to outlast the difference or a claimed burn would still get a second back slab.
    /// </summary>
    private const float ClaimSlackSeconds = 3f;

    private readonly RemoteAvatar _owner;

    /// <summary>A visible burn slab owns its origin recess through the hold and flight. The
    /// model's discard seat disappears at acceptance while the old covered recess can remain
    /// drawn, so ownership must include backs and be tested before that recess is drawn again.</summary>
    internal bool OwnsRecess(int recess)
    {
        if (recess < 0)
            return false;
        for (int i = 0; i < _burns.Count; i++)
        {
            Burn burn = _burns[i];
            if (burn.Active && OwnsDisplayedBoard(burn) && burn.Recess == recess && burn.Go != null && burn.Go.activeSelf
                && (!burn.HandoverLogged || _owner.OwnsFlightSlot(recess,
                    RemoteBoardFocus.ActorById(burn.ActorId), burn.FlightGeneration)))
                return true;
        }
        return false;
    }


    // Model commits precede the owner's animation. Keep every existing card seat and fan
    // population intact until the matching release has also drained native owner playback.
    // This includes a burn still drawn by its original recess (its detached slab is hidden).
    internal bool HasObservedBurn(CAbilityCard card)
    {
        foreach (var burn in _burns)
            if (burn.Active && !burn.HandoverLogged && burn.Widget != null
                && ReferenceEquals(burn.Widget.AbilityCard, card)) return true;
        return false;
    }

    internal bool HoldsCardLayout => PresentationActor != null;

    internal CPlayerActor? PresentationActor
    {
        get
        {
            foreach (Burn burn in _burns)
            {
                if (!burn.Active || burn.HandoverLogged) continue;
                CPlayerActor? actor = RemoteBoardFocus.ActorById(burn.ActorId);
                CAbilityCard? card = burn.Widget != null ? burn.Widget.AbilityCard : null;
                if (card != null && actor != null
                    && (actor.CharacterClass.LostAbilityCards.Contains(card)
                        || actor.CharacterClass.PermanentlyLostAbilityCards.Contains(card))) return actor;
            }
            foreach (PendingRelease pending in _pendingReleases)
            {
                if (pending.CompletionTime < 0f || pending.FallbackPlayed || pending.ActorId != _watchActor) continue;
                CPlayerActor? actor = RemoteBoardFocus.ActorById(pending.ActorId);
                if (actor != null && !PendingRecovered(pending, actor)) return actor;
            }
            return null;
        }
    }

    private bool PendingRecovered(PendingRelease pending, CPlayerActor actor)
    {
        CAbilityCard? card = pending.OriginalCard;
        if (card == null) return false;
        var cards = actor.CharacterClass;
        if (cards.LostAbilityCards.Contains(card) || cards.PermanentlyLostAbilityCards.Contains(card)) return false;
        bool returned = cards.HandAbilityCards.Contains(card) || cards.RoundAbilityCards.Contains(card)
            || cards.ActivatedCards.Contains(card);
        return returned && CardAppearanceMirror.HasRecoveredSourceAfter(
            pending.PresentationPlayer != 0 ? pending.PresentationPlayer : _owner.PlayerId,
            actor, card, pending.CompletionTime);
    }

    private bool OwnsDisplayedBoard(Burn burn) => BurnReleasePolicy.OwnsBoard(burn.ActorId,
        NetFigures.StableActorId(RemoteBoardFocus.DisplayedActor(_owner, out _)));

    internal bool OwnsActiveCard(int cardInstanceId)
    {
        foreach (Burn burn in _burns)
            if (burn.Active && burn.FromActive && burn.CardId == cardInstanceId
                && OwnsDisplayedBoard(burn) && burn.Go != null && burn.Go.activeSelf) return true;
        return false;
    }

    private sealed class Burn
    {
        public GameObject? Go;
        public RemoteCardArt? Art;
        public float Elapsed;
        public bool Active;
        public bool HasFace;
        public bool ShortRestBurn;
        public float ArtworkWait;
        public Vector3 From;
        public Vector3 To;
        public float Arc;

        /// <summary><c>CAbilityCard.CardInstanceID</c> of the card being burned, so the recess that
        /// is drawing it can be re-identified every frame rather than once.</summary>
        public int CardId;

        /// <summary>The recess this card was lying in when the burn was learned about (0/1), or -1.
        /// It decides both the ORIGIN of the flight and whether this presentation draws a slab at
        /// all during the hold — see <see cref="Drive"/>.</summary>
        public int Recess;
        public bool FromActive;
        public Vector3 ActiveLocal;

        /// <summary>Card name, kept only so the hand-over line can name the same card the
        /// <c>BURN CARD</c> line above it named.</summary>
        public string Name = "?";

        /// <summary>Frames the owner's recess was still DRAWING this card, i.e. the length of the
        /// hold this presentation actually observed rather than the one it used to run on a clock of
        /// its own. Zero means the card was never seated where this client could see it.</summary>
        public int SeatedFrames;

        /// <summary>This client's native counterpart, retained for model/artwork lookup. It is
        /// not the owner's running coroutine; the owner's semantic event authorizes release.</summary>
        public AbilityCardUI? Widget;

        /// <summary>Whether this client's native counterpart was observed animating. Diagnostic
        /// evidence only; absence cannot finish an owner's burn.</summary>
        public bool ArtworkObserved;

        /// <summary>Seconds this presentation's own slab stood STILL at the flight's origin before
        /// the arc. Non-zero only for a burn the owner's recess never drew — the card has to lie
        /// somewhere or the peer cannot "sehen dass die Karte kurz liegen bleibt".</summary>
        public float StationaryShown;

        /// <summary>The char this slab was last painted at during a STATIONARY hold frame, 0..1 -
        /// the owner's own <c>_GreyOut</c> when his ramp was observable here, and the settled 1 when
        /// it was not. -1 means no stationary frame ever ran (his recess drew the card throughout,
        /// or the burn flew at once).
        ///
        /// <para>THE DECIDING FIELD for R2's F5. This surface used to paint a hard 1 every frame of
        /// the hold, so "the viewer saw the char ramp" and "the viewer saw a black card standing
        /// still" produced identical logs. A value that ENDS near 1 beside a non-zero
        /// <see cref="StationaryShown"/> is a ramp that ran; a value pinned at exactly 1.00 from the
        /// first frame is the arm that could not read the owner's paint.</para></summary>
        public float StationaryCharred = -1f;

        /// <summary>Which <c>Claim</c> this presentation minted, or 0 for none. It is an
        /// identity and not a count, which is the whole of R3's F5: a bare counter cannot say WHICH
        /// burn a swallow belongs to, so a token stranded by a dropped packet could eat a later
        /// burn's event. See <see cref="ConsumesWireEvent"/>.</summary>
        public int ClaimId;
        public int ActorId;
        public bool OwnerReleased;
        public float CompletionTime = -1f;
        public int PresentationPlayer;
        public long FlightGeneration;

        /// <summary>Has the arc been reported? One line per burn, at the hand-over instant.</summary>
        public bool HandoverLogged;

        /// <summary>What this slab's card BODY is wearing on its FRONT fan (true = the card back) —
        /// the edge gate for <see cref="SetFrontFace"/>. Seeded true because that is what
        /// <see cref="Acquire"/> builds it with.</summary>
        public bool WearsBack = true;

        /// <summary>The owner's own card WIDTH at this burn's ORIGIN and at the burnt pile, in
        /// metres. The slab ramps between them across the arc on the SAME eased term the position
        /// uses — see <see cref="Drive"/> and the block in <see cref="Present"/> that seeds them, and
        /// <c>RemoteCardFx.WidthForAnchor</c>, whose rule this is.</summary>
        public float FromWidth = RemoteHandFan.DefaultCardWidth;
        public float ToWidth = RemoteHandFan.DefaultCardWidth;
    }

    private readonly List<Burn> _burns = new(MaxBurns);
    private GameObject? _root;

    // ---- the model watch (the whole identity story) ------------------------------------------
    private readonly List<AbilityCardUI> _burntBuf = new(16);
    private readonly HashSet<AbilityCardUI> _known = new();
    private readonly List<AbilityCardUI> _pruneScratch = new(4);
    private int _watchActor;                 // stable id of the actor the baseline belongs to
    private bool _seeded;
    private float _nextWalkAt;

    /// <summary>Unscaled time and frame of the PREVIOUS burnt-pile walk. THE DELAY LIVES HERE and
    /// report item 7 names it ("diese war verzögert"): this class learns of a burn by POLLING the
    /// host-replicated Lost pile at <see cref="WatchSeconds"/>, so the burn happened
    /// somewhere inside the window these two numbers bound. Printed on the BURN CARD line rather
    /// than reasoned about, because "the poll cadence" and "the packet was late" look identical in
    /// a log without them.</summary>
    private float _lastWalkAt = float.NegativeInfinity;
    private int _lastWalkFrame = -1;

    /// <summary>Seconds between the walk that found the current burn and the one before it (-1 on
    /// the very first walk) — the upper bound on how long this mirror sat on a burn it had not
    /// looked for yet.</summary>
    private float _sinceLastWalk = -1f;

    /// <summary>Unscaled time of the most recent presentation start — the mint stamp each
    /// <see cref="Claim"/> takes a copy of. It is deliberately no longer a class-wide claim ANCHOR:
    /// one clock shared by every token is the defect the token list replaced.</summary>
    private float _lastPresentedAt = float.NegativeInfinity;

    /// <summary>
    /// ONE OUTSTANDING SWALLOW, BOUND TO THE PRESENTATION THAT MINTED IT.
    ///
    /// <para>WHAT THIS REPLACED, AND WHY (2026-09-07 review R3, F5). The claim used to be a bare
    /// <c>int</c>. Nothing bound a token to a card, and the whole set shared ONE expiry anchored on
    /// the most recent presentation or hand-over — so a token stranded by a dropped packet (the
    /// extras stream is unreliable by contract and this mod's own <c>CARD FX LOST</c> line has
    /// measured losses) stayed spendable for as long as any OTHER burn kept refreshing that shared
    /// anchor, and could then eat the event for a burn this mirror never presented. That burn's
    /// fallback flight — the anonymous back slab <see cref="RemoteCardFx"/> would have drawn — was
    /// the thing lost, i.e. an animation the owner played and the viewer never saw.</para>
    ///
    /// <para>Live holds have no elapsed-time ceiling. The owner's completed native iterator
    /// and the receiver's exact causal appearance frame authorize departure. A paused game
    /// clock or delayed transport may legitimately keep this claim alive for longer.</para>
    ///
    /// <para>EACH TOKEN NOW CARRIES ITS OWN CLOCK. It cannot expire while its presentation is still
    /// holding (the owner has by definition not cleared his card yet, so his event cannot have been
    /// sent), and once that presentation hands over it expires on ITS OWN hand-over instant rather
    /// than on the newest one — which is exactly the difference between "each presented burn eats
    /// its own event" and "the set eats whatever arrives".</para>
    /// </summary>
    private sealed class Claim
    {
        /// <summary>Matches <c>Burn.ClaimId</c>. Never 0 for a live token.</summary>
        public int Id;
        public int ActorId;
        public int Recess;
        public bool FromActive;
        public CardFlightSource? Source;

        /// <summary>The card this token was minted for — carried so the expiry line can NAME the
        /// burn whose event never arrived, which is the only thing that turns "the leak is
        /// plausible" into a reading.</summary>
        public string Name = "?";

        /// <summary>Unscaled time of the <see cref="Present"/> that minted it.</summary>
        public float MintedAt;

        /// <summary>Unscaled time of that presentation's hand-over, or <c>NegativeInfinity</c> while
        /// it is still holding.</summary>
        public float ReleasedAt = float.NegativeInfinity;
    }

    /// <summary>Outstanding swallows, oldest first (append order = mint order). Bounded by the
    /// expiry in <see cref="PruneClaims"/>, which runs on both of the two event-driven paths that
    /// can grow or read it.</summary>
    private readonly List<Claim> _claimTokens = new(MaxBurns);

    /// <summary>Source of <see cref="Claim.Id"/>. Monotonic per avatar; it is an identity, never an
    /// index.</summary>
    private int _nextClaimId;

    /// <summary>How long a token stays spendable after ITS OWN presentation handed over. The owner
    /// reports his <c>Board -&gt; Burnt</c> flight when his card is really cleared, which is the same
    /// signal this mirror's hand-over reads, so the slack only has to cover the difference between
    /// the two — not the whole turn the old shared anchor had to survive.</summary>
    private static float ClaimWindowSeconds =>
        HoldSeconds + NetProtocol.CardFxSeconds + ClaimSlackSeconds;

    /// <summary>Drop every token whose own window has run out, naming the burn it belonged to.
    /// Cheap by construction: at most <see cref="MaxBurns"/> presentations can be live at once and a
    /// token outlives its presentation by <see cref="ClaimWindowSeconds"/>, so this list is a
    /// handful of entries and is walked only when one is minted or spent.</summary>
    private void PruneClaims()
    {
        float now = Time.unscaledTime;
        for (int i = _claimTokens.Count - 1; i >= 0; i--)
        {
            Claim c = _claimTokens[i];
            bool holding = float.IsNegativeInfinity(c.ReleasedAt);
            // A HOLDING token never expires on the window — the owner has not sent its event yet.
            // The belt is his own deadline: BurnArtwork.Released lands every hold at
            // MaxHoldSeconds, so a token still holding past that plus the window belongs to a
            // presentation that stopped ticking (a slab torn down under it), not to a live burn.
            float age = holding ? now - c.MintedAt : now - c.ReleasedAt;
            float allowed = holding ? BurnArtwork.MaxHoldSeconds + ClaimWindowSeconds
                                    : ClaimWindowSeconds;
            bool liveHold = false;
            if (holding)
                for (int b = 0; b < _burns.Count; b++)
                    if (_burns[b].Active && !_burns[b].HandoverLogged && _burns[b].ClaimId == c.Id)
                        liveHold = true;
            if (liveHold || age <= allowed)
                continue;
            _claimTokens.RemoveAt(i);
            // HW-VERIFY: grep token "BURN CLAIM EXPIRED". It says ONE thing and only that thing:
            // this mirror presented that burn and the owner's matching Board -> Burnt event never
            // arrived within the window. It does NOT name a cause — a dropped extras packet and a
            // sender suppressed by a read-only character focus produce the identical reading, and
            // the way to tell them apart is the peer's own 'CARD FX LOST' count in the same window.
            // Its absence across a session is the falsifier for R3's F5 picture; its presence
            // BESIDE a later burn that flew as an anonymous back slab is the confirmation.
            VRLog.Note("Net", $"BURN CLAIM EXPIRED [peer {_owner.PlayerId}]: this mirror presented " +
                              $"'{c.Name}' and swallowed nothing for it — the owner's matching " +
                              $"'-> Burnt' card-FX event never arrived in the " +
                              $"{allowed:F1}s after its {(holding ? "discovery" : "hand-over")}. " +
                              "The token is dropped rather than left spendable, so it cannot eat a " +
                              "LATER burn's event and cost that burn the fallback slab flight " +
                              "RemoteCardFx would otherwise have drawn. Compare 'CARD FX LOST', " +
                              "'EXTRAS TRANSPORT' and the owner's release log in the same interval; " +
                              "this expired claim alone does not establish where the event was lost.");
        }
    }

    /// <summary>Stamp <paramref name="claimId"/>'s token as released, so its own window starts from
    /// this instant instead of from whichever presentation happened to be newest.</summary>
    private void ReleaseClaim(int claimId)
    {
        if (claimId == 0)
            return;
        for (int i = 0; i < _claimTokens.Count; i++)
        {
            if (_claimTokens[i].Id != claimId)
                continue;
            _claimTokens[i].ReleasedAt = Time.unscaledTime;
            return;
        }
    }

    /// <summary>Drop <paramref name="claimId"/>'s token outright — the presentation it was minted
    /// for is being abandoned, so the owner's event must be allowed THROUGH to
    /// <see cref="RemoteCardFx"/> rather than swallowed for a flight this viewer never saw.</summary>
    private void DropClaim(int claimId)
    {
        if (claimId == 0)
            return;
        for (int i = 0; i < _claimTokens.Count; i++)
        {
            if (_claimTokens[i].Id != claimId)
                continue;
            _claimTokens.RemoveAt(i);
            return;
        }
    }

    private int _played;

    internal RemoteBurnFx(RemoteAvatar owner)
    {
        _owner = owner;
    }

    // ------------------------------------------------------------------ the wire hand-off --

    // The owner releases a burn by sending the existing semantic ->Burnt event. The receiving
    // client's native widget is NOT the owner's widget: MB482 LeapingCleave held 1.99s locally
    // while this mirror saw no artwork and flew at 0.51s. Reading the same API on two different
    // UI instances is not synchronization. Match the event by actor and origin recess instead.
    private sealed class PendingRelease
    {
        public byte Endpoints;
        public byte Flags;
        public CardFlightSource? Source;
        public int ActorId;
        public float ReceivedAt;
        public float CompletionTime = -1f;
        public int PresentationPlayer;
        public bool FallbackPlayed;
        public CAbilityCard? OriginalCard;
    }

    private readonly List<PendingRelease> _pendingReleases = new(MaxBurns);
    private const float ReleaseResolveSeconds = 0.25f;
    private const float ReleaseMemorySeconds = 3f;

    internal bool ConsumesWireEvent(byte endpoints, byte flags = 0, CardFlightSource? source = null, float completionTime = -1f, int presentationPlayer = 0, CAbilityCard? originalCard = null)
    {
        if (NetCardFx.To(endpoints) != CardFxAnchor.Burnt)
            return false;
        int actorId = source?.ActorId ?? NetFigures.StableActorId(RemoteBoardFocus.DisplayedActor(_owner, out _));
        if (TryApplyRelease(endpoints, actorId, flags, source, completionTime, presentationPlayer, originalCard))
            return true;
        // The semantic packet can beat the host-replicated pile. Defer its fallback briefly so
        // the next model walk can name the real card, then retain a receipt after fallback to
        // prevent that late model discovery from drawing the same flight twice.
        if (_pendingReleases.Count >= MaxBurns)
        {
            PendingRelease oldest = _pendingReleases[0];
            if (!oldest.FallbackPlayed && oldest.CompletionTime < 0f && (oldest.Source.HasValue
                || oldest.ActorId == NetFigures.StableActorId(RemoteBoardFocus.DisplayedActor(_owner, out _))))
                _owner.PlayUnclaimedBurnEvent(oldest.Endpoints, oldest.Flags, oldest.Source);
            _pendingReleases.RemoveAt(0);
        }
        _pendingReleases.Add(new PendingRelease
        {
            Endpoints = endpoints, Flags = flags, Source = source, ActorId = actorId, ReceivedAt = Time.unscaledTime,
            CompletionTime = completionTime, PresentationPlayer = presentationPlayer,
            OriginalCard = originalCard ?? _owner.PresentedBurnSource(endpoints, source, actorId),
        });
        _nextWalkAt = 0f;
        return true;
    }

    private static int ReleaseRecess(byte endpoints) => NetCardFx.From(endpoints) switch
    {
        CardFxAnchor.Slot0 => 0,
        CardFxAnchor.Slot1 => 1,
        _ => -1,
    };

    private bool TryApplyRelease(byte endpoints, int actorId, byte flags, CardFlightSource? source, float completionTime, int presentationPlayer, CAbilityCard? originalCard)
    {
        PruneClaims();
        int recess = ReleaseRecess(endpoints);
        int match = -1;
        for (int i = 0; i < _claimTokens.Count; i++)
        {
            Claim claim = _claimTokens[i];
            if (originalCard != null)
            {
                Burn? original = _burns.Find(b => b.Active && b.ClaimId == claim.Id);
                if (claim.ActorId != actorId || original?.Widget == null
                    || !ReferenceEquals(original.Widget.AbilityCard, originalCard)) continue;
            }
            else if (!BurnReleasePolicy.Matches(claim.ActorId, claim.FromActive, claim.Recess, claim.Source,
                actorId, NetCardFx.From(endpoints), source)) continue;
            // A Board origin still cannot distinguish two simultaneous unnamed burns.
            if (match >= 0)
                return false;
            match = i;
        }
        if (match < 0)
            return false;
        int id = _claimTokens[match].Id;
        _claimTokens.RemoveAt(match);
        for (int i = 0; i < _burns.Count; i++)
        {
            Burn burn = _burns[i];
            if (burn.ClaimId != id || !burn.Active)
                continue;
            // The release's provenance supersedes the earlier model-watch context. An ordinary
            // burn must not inherit a different short-rest offer that appeared before delivery.
            burn.ShortRestBurn = CardFlightVisibility.Covered(flags);
            RefreshFaceVisibility(burn);
            if (originalCard != null)
            {
                burn.Recess = ReleaseRecess(endpoints);
                burn.FromActive = NetCardFx.From(endpoints) == CardFxAnchor.Active;
                if (burn.FromActive) RemoteActiveDepartures.TryPose(_owner.PlayerId, originalCard, out burn.ActiveLocal);
            }
            burn.OwnerReleased = true;
            burn.CompletionTime = completionTime;
            burn.PresentationPlayer = presentationPlayer != 0 ? presentationPlayer : _owner.PlayerId;
            if (burn.Widget != null)
                burn.Art?.SetNativeAppearance(burn.PresentationPlayer, RemoteBoardFocus.ActorById(burn.ActorId), burn.Widget.AbilityCard);
            break;
        }
        return true;
    }

    private void TickPendingReleases()
    {
        int actorId = NetFigures.StableActorId(RemoteBoardFocus.DisplayedActor(_owner, out _));
        for (int i = _pendingReleases.Count - 1; i >= 0; i--)
        {
            PendingRelease pending = _pendingReleases[i];
            float age = Time.unscaledTime - pending.ReceivedAt;
            if ((!pending.Source.HasValue && pending.ActorId != actorId) || pending.CompletionTime < 0f && age > ReleaseMemorySeconds
                || pending.CompletionTime >= 0f && RemoteBoardFocus.ActorById(pending.ActorId) == null)
            {
                _pendingReleases.RemoveAt(i);
                continue;
            }
            CPlayerActor? pendingActor = RemoteBoardFocus.ActorById(pending.ActorId);
            if (pending.CompletionTime >= 0f && pendingActor != null && PendingRecovered(pending, pendingActor))
            {
                _pendingReleases.RemoveAt(i);
                continue;
            }
            if (pending.FallbackPlayed)
                continue;
            if (TryApplyRelease(pending.Endpoints, pending.ActorId, pending.Flags, pending.Source, pending.CompletionTime, pending.PresentationPlayer, pending.OriginalCard))
            {
                _pendingReleases.RemoveAt(i);
                continue;
            }
            if (age >= ReleaseResolveSeconds && pending.CompletionTime < 0f)
            {
                _owner.PlayUnclaimedBurnEvent(pending.Endpoints, pending.Flags, pending.Source);
                pending.FallbackPlayed = true;
            }
        }
    }

    private bool AlreadyFlewEarlyRelease(int actorId, int recess, bool fromActive, CardFlightSource? source)
    {
        for (int i = 0; i < _pendingReleases.Count; i++)
        {
            PendingRelease pending = _pendingReleases[i];
            if (pending.ActorId != actorId || !pending.FallbackPlayed
                || Time.unscaledTime - pending.ReceivedAt > ReleaseMemorySeconds
                || !BurnReleasePolicy.Matches(actorId, fromActive, recess, source,
                    pending.ActorId, NetCardFx.From(pending.Endpoints), pending.Source))
                continue;
            _pendingReleases.RemoveAt(i);
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ per frame --

    private CAbilityCard? _shortRestCandidate;
    private int _shortRestActor;
    private int _shortRestRound;

    // Called before a packet replaces the old short-rest/seat snapshot, and every native-model
    // tick. This identity stays local: record 39 already supplied the bounded model address.
    internal void ObserveShortRestContext()
    {
        PruneShortRestCandidate();
        if (!_owner.ShortRestInProgress) return;
        // The current local short-rest presentation uses recess 0; inspect the complete existing
        // record 39 vocabulary so an alternate original seat cannot lose the same provenance.
        for (int recess = 0; recess < 2; recess++)
        {
            if (!_owner.TryNameArrivingRecessFace(recess, out CAbilityCard? card) || card == null) continue;
            _shortRestCandidate = card;
            _shortRestActor = NetFigures.StableActorId(RemoteBoardFocus.DisplayedActor(_owner, out _));
            _shortRestRound = CardsGameApi.RoundNumber();
            break;
        }
    }

    private void PruneShortRestCandidate()
    {
        if (_shortRestCandidate == null) return;
        CCharacterClass? cc = RemoteBoardFocus.ActorById(_shortRestActor)?.CharacterClass;
        bool returned = cc != null && (cc.HandAbilityCards.Contains(_shortRestCandidate)
            || cc.RoundAbilityCards.Contains(_shortRestCandidate) || cc.ActivatedCards.Contains(_shortRestCandidate));
        bool lost = cc != null && (cc.LostAbilityCards.Contains(_shortRestCandidate)
            || cc.PermanentlyLostAbilityCards.Contains(_shortRestCandidate));
        int round = CardsGameApi.RoundNumber();
        bool laterRound = round > 0 && _shortRestRound > 0 && round != _shortRestRound;
        if (!CardFlightVisibility.KeepObservedCandidate(cc != null, returned, lost, laterRound))
            _shortRestCandidate = null;
    }

    internal void PreparePresentation()
    {
        try
        {
            ObserveShortRestContext();
            Watch();
        }
        catch (System.Exception ex)
        {
            // Never throw out of the avatar tick: an unguarded throw there starves VR input.
            _seeded = false;
            VRLog.Warn("Net", $"Remote burn watch [player {_owner.PlayerId}] threw ({ex.Message}) — " +
                              "the baseline is re-seeded silently, so no burn storms when it recovers.");
        }
        TickPendingReleases();
    }

    internal void Tick(float dt)
    {
        PreparePresentation();
        Drive(dt);
    }

    /// <summary>
    /// Diff the displayed character's BURNT pile against the previous walk. A new entry is a burn
    /// this client has just learned about, at the same instant the owner's own watcher learns it —
    /// both read the same host-replicated list.
    ///
    /// <para>THE BASELINE IS SEEDED SILENTLY on the first walk and on every actor change, exactly
    /// as <c>CardsDriver.TickBurnToPile</c> re-seeds <c>_knownBurntWidgets</c> on a hand change and
    /// for the same reason: a character's long-burned cards must never animate retroactively when
    /// the board starts presenting them.</para>
    ///
    /// <para>It keeps walking while the peer's board is HIDDEN and simply presents nothing, so
    /// switching <c>[Net] RemoteBoards</c> back on cannot replay a scenario's worth of burns.</para>
    /// </summary>
    private int _modelStamp;
    private int _preparedFrame = -1;
    private void Watch()
    {
        if (_preparedFrame == Time.frameCount) return;
        _preparedFrame = Time.frameCount;
        CPlayerActor? sampledActor = RemoteBoardFocus.DisplayedActor(_owner, out _);
        int stamp = NetFigures.StableActorId(sampledActor);
        if (sampledActor?.CharacterClass != null)
        {
            unchecked
            {
                foreach (var card in sampledActor.CharacterClass.LostAbilityCards)
                    stamp = stamp * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(card);
                foreach (var card in sampledActor.CharacterClass.PermanentlyLostAbilityCards)
                    stamp = stamp * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(card);
            }
        }
        // Inspect cheap model identities every frame, and only pay the native widget walk on
        // a changed population or the existing recovery cadence. Same-count replacements count.
        bool changed = stamp != _modelStamp;
        _modelStamp = stamp;
        if (!changed && Time.unscaledTime < _nextWalkAt)
            return;
        _sinceLastWalk = _lastWalkFrame < 0 ? -1f : Time.unscaledTime - _lastWalkAt;
        _lastWalkAt = Time.unscaledTime;
        _lastWalkFrame = Time.frameCount;
        _nextWalkAt = Time.unscaledTime + WatchSeconds;

        CPlayerActor? actor = RemoteBoardFocus.DisplayedActor(_owner, out _);
        int actorId = NetFigures.StableActorId(actor);
        CardsHandManager manager = CardsHandManager.Instance;
        CardsHandUI? hand = actor != null && manager != null ? manager.GetHand(actor) : null;
        if (hand == null)
        {
            // No hand on this client for that character — forget the baseline rather than diffing
            // against a stale one when it comes back.
            _seeded = false;
            _known.Clear();
            _watchActor = 0;
            return;
        }
        if (actorId != _watchActor)
        {
            // MB490: keep the old actor's claim, without aliasing it to the new focus. The
            // owner release already carries its original actor in record61. Reusing a recess
            // number on another character is neither completion nor ownership of that seat.
            _watchActor = actorId;
            _seeded = false;
            _known.Clear();
        }

        _burntBuf.Clear();
        CardsGameApi.GetPileWidgets(hand, burnt: true, _burntBuf);

        if (!_seeded)
        {
            _seeded = true;
            for (int i = 0; i < _burntBuf.Count; i++)
            {
                if (_burntBuf[i] != null)
                    _known.Add(_burntBuf[i]);
            }
            return;
        }

        for (int i = 0; i < _burntBuf.Count; i++)
        {
            AbilityCardUI widget = _burntBuf[i];
            if (widget == null || !_known.Add(widget))
                continue;
            // The SAME membership expression the owner's own browse arc and the mirrored pile fan
            // apply — a long-rest placeholder or a widget with no model card is not a burn.
            if (!CardsGameApi.PileWidgetIsArcMember(widget))
                continue;
            Present(widget, actor);
        }

        // Drop anything that LEFT the pile (a recovered lost card) so a re-burn animates again.
        // Through a reused scratch list rather than RemoveWhere: this runs per peer at the board
        // cadence and a lambda that captures `this` allocates a delegate on every call.
        _pruneScratch.Clear();
        // Membership in the game's own list is the whole test: a widget the game has destroyed is
        // no longer in it either, so a separate Unity-null test would only add a branch (and a
        // nullable-flow suppression) for a case this already covers.
        foreach (AbilityCardUI k in _known)
        {
            if (!_burntBuf.Contains(k))
                _pruneScratch.Add(k);
        }
        for (int i = 0; i < _pruneScratch.Count; i++)
            _known.Remove(_pruneScratch[i]);
        _pruneScratch.Clear();
    }

    /// <summary>Start one burn presentation for <paramref name="widget"/>.</summary>
    private void Present(AbilityCardUI widget, CPlayerActor? actor)
    {
        string name = CardsGameApi.CardName(widget);

        // BOARD FURNITURE. Both endpoints of this presentation are on the owner's board, so a
        // viewer who has switched that board off must not get a card burning in mid-air — the same
        // rule and the same predicate RemoteCardFx applies to every flight that touches the board.
        if (!RemoteBoardGate.ShowBoardSurface(_owner) || !_owner.HasBoard)
        {
            LogSkipped(name, "this viewer is not showing that peer's board right now");
            return;
        }

        // ─── WHERE THE OWNER'S CARD ACTUALLY IS (2026-09-06 follow-up, corrected for item 7) ─────
        // It is in a RECESS, not at the board centre: CardsDriver holds the played card on its slot
        // anchor while the game's burn artwork runs on it and only then flies it to the stack. This
        // mirror used to present at CardFxAnchor.Board because nothing named the recess — but this
        // client SEATED that card there itself, so it can simply ask. No wire field is owed: the
        // anchor vocabulary already has Slot0/Slot1 and RemoteControlBoard.AnchorLocalLive resolves
        // both to the rendered card SEAT of whatever board style the peer runs.
        //
        // IT ASKS RecessOfBurningCard, NOT RecessShowingCard, AND THAT WAS THE WHOLE OF ITEM 7.
        // RecessShowingCard answers "which recess is drawing this card's FACE", which is an
        // IDENTITY question standing in for a POSITION one — so a recess that is occupied but
        // drawing an anonymous back places the card nowhere. The ModBuild 461 log has exactly two
        // mirrored burns and that happened on ONE of them: 'BURN CARD ... at their board CENTRE'
        // beside 'round-card faces=anon-back/empty, slot-occupancy=0x1'. A fallback documented as
        // being for "a burn nobody could place" fired on half the burns in the round.
        int cardId = CardInstanceIdOf(widget);
        int recess = _owner.RecessOfBurningCard(cardId, out string recessHow);
        bool fromActive = widget.AbilityCard != null
            && RemoteActiveDepartures.TryPose(_owner.PlayerId, widget.AbilityCard, out _);
        CardFxAnchor origin = recess == 0 ? CardFxAnchor.Slot0
            : recess == 1 ? CardFxAnchor.Slot1
            : CardFxAnchor.Board;

        if (!TryAnchor(origin, out Vector3 from)
            || !TryAnchor(CardFxAnchor.Burnt, out Vector3 to))
        {
            LogSkipped(name, "their board pose has not arrived yet, so there is nowhere to burn it");
            return;
        }

        int actorId = NetFigures.StableActorId(actor);
        if (AlreadyFlewEarlyRelease(actorId, recess, fromActive,
            widget.AbilityCard != null ? RemoteActiveDepartures.SourceOf(_owner.PlayerId, widget.AbilityCard) : null))
            return; // the early semantic event already showed this flight; never replay it
        Burn b = Acquire();
        if (b.Go == null)
            return;
        b.CardId = cardId;
        b.ActorId = actorId;
        b.OwnerReleased = false;
        b.CompletionTime = -1f;
        b.PresentationPlayer = _owner.PlayerId;
        b.ArtworkWait = 0f;
        b.Recess = recess;
        b.Name = name;
        b.SeatedFrames = 0;
        b.ClaimId = 0;              // a recycled slab must not carry the last burn's token
        b.HandoverLogged = false;
        b.Widget = widget;
        if (widget.AbilityCard != null)
            b.Art?.SetNativeAppearance(_owner.PlayerId, RemoteBoardFocus.ActorById(actorId), widget.AbilityCard);
        b.FromActive = fromActive;
        b.ActiveLocal = default;
        if (fromActive && widget.AbilityCard != null)
            RemoteActiveDepartures.TryPose(_owner.PlayerId, widget.AbilityCard, out b.ActiveLocal);
        b.ShortRestBurn = _owner.ShortRestInProgress
            || (_shortRestActor == actorId && ReferenceEquals(_shortRestCandidate, widget.AbilityCard));
        if (_shortRestActor == actorId && ReferenceEquals(_shortRestCandidate, widget.AbilityCard))
            _shortRestCandidate = null;
        b.ArtworkObserved = false;
        b.StationaryShown = 0f;
        b.StationaryCharred = -1f;

        // The board AS DRAWN, not the wire target — see DrawnBoardScale. Read once here for
        // the arc FLOOR and the slab's opening size; Drive re-reads it every frame.
        float scale = DrawnBoardScale;
        float cardWidth = Mathf.Max(0.01f, _owner.BoardTuning.CardWidth);
        float cardHeight = cardWidth * (88f / 63.5f);

        b.Elapsed = 0f;
        b.Active = true;
        b.From = from;
        b.To = to;
        // CardsDriver.BoardArcMin and VRCard.FlyArcHeightFraction, the same two terms RemoteCardFx
        // resolves for every other mirrored flight — read its ArcFraction note before touching
        // either number, they are code literals that simply have to be the same on both sides.
        b.Arc = Mathf.Max(cardHeight * 1.5f * scale, Vector3.Distance(from, to) * VRCard.FlyArcHeightFraction);
        // ─── ONE CONSTANT SCALE FOR A RAMPED ARC — the same defect ModBuild 477 item 8 fixed one
        //     class over, still standing here. ("Die anderen Spieler am remote board sehen beim Flug
        //     kurz die offene ... kleine Karte in der Karte clippen, und dann zum stapel fliegen.")
        //
        // This line used to be the ONLY scale write in the class, taken from the owner's HAND card
        // width, and Drive never touched it again. The owner's own burn does not hold its size:
        // VRCard.FlyToPile captures the DOCKED scale and ramps to the pile-slab scale across the
        // arc, so a card leaving a round recess starts at THAT recess card's size.
        //
        // IT FIRED ON EVERY BURN OF THE ModBuild 476 SESSION, AND THE GAP IS 2.5x. All five
        // mirrored burns across the two logs (3 host + 2 peer) report 'DURING THE HOLD the card was
        // drawn by their recess 1', so every one of them had a recess origin; and both clients'
        // 'Slot-card size RECEIVED' line reads card 156.8 mm against a hand CardWidth of 63.5 mm.
        // The slab therefore lifted out of the recess at 40 % of the size of the card already
        // sitting in that very seat — a small card inside a card, clipping through it, for the
        // length of the hold and the arc.
        //
        // SAME RULE, SAME FIELDS, SAME SOURCE as RemoteCardFx.WidthForAnchor, which is where the
        // argument is written out in full. A RECESS end is the owner's own slot card width (record
        // ExtIdSlotCardSize, RemoteAvatar.SlotCardWidth — the identical number RemoteControlBoard
        // sizes its recess cards with). The BURNT end is that stack's SLAB
        // (PileViewer.PileStack.SlabFactor = 0.62 of the card width), which is the product
        // RemoteControlBoard.PileCounter draws the mirrored stack at and the one the owner's own
        // PileViewer.TryGetPileWorld hands FlyToPile — so the card shrinks into the stack here
        // exactly as it does there, instead of vanishing over it at full size. No new wire field.
        float slotWidth = _owner.SlotCardWidth;
        b.FromWidth = b.FromActive ? cardWidth * Mathf.Max(0.01f, _owner.BoardTuning.ActiveCardScale)
            : b.Recess >= 0 && slotWidth > 0.001f ? slotWidth : cardWidth;
        b.ToWidth = cardWidth * PileViewer.PileStack.SlabFactor;
        b.Go.transform.localScale = Vector3.one * (scale * (b.FromWidth / RemoteHandFan.DefaultCardWidth));
        // LYING ON THEIR BOARD, not billboarded at us: the owner's card rests in a recess of a
        // board this client already knows the rotation of, and FlyToPile holds that orientation for
        // the whole flight ("orientation locked"). Facing it at the local head instead would be a
        // pose the owner never sees.
        b.Go.transform.SetPositionAndRotation(from, DrawnBoardRotation);
        // …AND IT STARTS HIDDEN, ALWAYS. Drive() is the one place that reveals it, and it reveals it
        // for exactly two pictures: the ARC, and — since ModBuild 475, item 8's "die sehen auch dass
        // die Karte kurz liegen bleibt" — the owner's own hold on a burn NO recess is drawing.
        // Never both, and never while the recess draws the card: two copies of one card is a worse
        // divergence than the one this class was built to fix, and the recess copy is the better of
        // the two by construction — it is the card the owner is looking at, in the recess he is
        // looking at, wearing the char RemoteBoardCard.DriveUsedCardFx is ramping on it.
        if (b.Go.activeSelf)
            b.Go.SetActive(false);

        // THE FACE — resolved locally, gated exactly as every other remote card surface is.
        b.HasFace = false;
        bool fronts = false;
        RevealGate.FaceRule faceRule = RevealGate.FaceRule.NoContext;
        try
        {
            // Model membership resolves the source only. Selection covers every burn, and the
            // short-rest origin is latched so its flight cannot open at the following phase edge.
            bool idKnown = cardId != int.MinValue;
            fronts = !b.ShortRestBurn && actor != null
                     && RevealGate.CardFaces(
                            idKnown ? RevealGate.PeerCardPopulation.Selectable
                                    : RevealGate.PeerCardPopulation.AlreadyPublic,
                            actor, cardId, out faceRule)
                        != RevealGate.CardFaceSource.None;
            if (fronts && widget.AbilityCard != null && b.Art != null)
                b.HasFace = RemoteAbilityCardSource.ShowFullFace(b.Art, actor, widget.AbilityCard)
                    != RemoteAbilityCardSource.FacePath.None;
        }
        catch (System.Exception ex)
        {
            b.HasFace = false;
            VRLog.Warn("Net", $"Remote burn face [player {_owner.PlayerId}]: '{name}' front resolve " +
                              $"failed ({ex.Message}) — burning a card BACK, which is what every build " +
                              "before this one showed.");
        }
        if (!b.HasFace)
            b.Art?.HideFront();
        // …AND WHAT THE BODY UNDER THE PRINT WEARS (user item 10, 2026-09-06 late). Read off the
        // SAME expression that decides whether a front is drawn at all — the HideFront above — and
        // not off b.HasFace later in the flight, because Drive() re-uses that field to mean "the
        // burn rig is still driving this face" and clears it while the print is still up. One
        // decision, expressed once. CardMesh.SetBodyFrontFace owns the rule and the fade-aware
        // write; a burn showing a BACK keeps the back on both submeshes.
        SetFrontFace(b, showsBack: !b.HasFace);

        _played++;
        _lastPresentedAt = Time.unscaledTime;
        // ONE TOKEN, BOUND TO THIS PRESENTATION. Pruned first so a session's worth of unanswered
        // tokens cannot accumulate on a peer whose events are all being dropped: this and
        // ConsumesWireEvent are the only two paths that touch the list, and both sweep it.
        PruneClaims();
        b.ClaimId = ++_nextClaimId;
        _claimTokens.Add(new Claim
        {
            Id = b.ClaimId, Name = name, MintedAt = _lastPresentedAt,
            ActorId = actorId, Recess = recess,
            FromActive = fromActive,
            Source = widget?.AbilityCard != null ? RemoteActiveDepartures.SourceOf(_owner.PlayerId, widget.AbilityCard) : null,
        });
        // THE SAME CENSUS POPULATION RemoteCardFx REPORTS (2026-09-06 report item 5). A burn IS a
        // card flying into a stack, and it reaches the viewer through this class instead of that one
        // only because ConsumesWireEvent swallowed the event — an implementation split, not a
        // different picture. Reporting from both is what makes "flight slab" a statement about every
        // mirrored flight rather than about the ones one of the two classes happened to draw.
        PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.FlightSlab, _owner.PlayerId,
            b.HasFace ? 1 : 0, b.HasFace ? 0 : 1,
            b.HasFace
                ? "BURN: FRONT off this client's own copy of that character's LostAbilityCards — "
                  + RevealGate.RuleText(faceRule)
                : b.ShortRestBurn
                    ? "BURN: COVERED — short-rest origin remains covered for its complete flight"
                : fronts
                    ? "BURN: RevealGate was OPEN but the front did not resolve — read the "
                      + "'BURN CARD [peer n]' line beside this for which half failed"
                    : "BURN: RevealGate.CardFaces refused this card. THIS FILE NEVER CALLS "
                      + "ShowRoundCardFronts and the sentence that used to stand here said it did; "
                      + "the rule that actually decided is: " + RevealGate.RuleText(faceRule)
                      + (actor == null
                          ? " (and there was no displayed actor at all, which is a CAPABILITY "
                            + "failure and not a secrecy verdict)"
                          : string.Empty));
        LogAttribution(name, fronts, b.HasFace, actor, recess, recessHow);
    }

    private static bool CanDrawBurn(Burn b) => b.ShortRestBurn
        || RevealGate.IsSecretSelectionPhase || b.HasFace;

    private static void RefreshFaceVisibility(Burn b)
    {
        CPlayerActor? actor = RemoteBoardFocus.ActorById(b.ActorId);
        bool allowed = !b.ShortRestBurn && RevealGate.CardFaces(RevealGate.PeerCardPopulation.Selectable,
            actor, b.CardId, scenarioEstablished: true, out _) != RevealGate.CardFaceSource.None;
        if (!allowed)
        {
            b.Art?.HideFront();
            b.HasFace = false;
        }
        else if (!b.HasFace && b.Art != null && b.Widget?.AbilityCard != null)
            b.HasFace = RemoteAbilityCardSource.ShowFullFace(b.Art, actor, b.Widget.AbilityCard)
                != RemoteAbilityCardSource.FacePath.None;
        SetFrontFace(b, showsBack: !b.HasFace);
    }

    /// <summary>Advance every live presentation: hold with the burn ramping on, then the arc.</summary>
    private void Drive(float dt)
    {
        float step = Mathf.Max(dt, 0f);
        for (int i = 0; i < _burns.Count; i++)
        {
            Burn b = _burns[i];
            if (!b.Active) continue;
            if (b.Go == null)
            {
                DropClaim(b.ClaimId);
                b.Active = false;
                continue;
            }
            // Recovery/undo can remove a lost card before an owner release. Such a hold must
            // disappear immediately, never retain a claim or suppress a newly seated card.
            CPlayerActor? actor = RemoteBoardFocus.ActorById(b.ActorId);
            CAbilityCard? card = b.Widget != null ? b.Widget.AbilityCard : null;
            if (!b.HandoverLogged && (actor == null || card == null
                || (!actor.CharacterClass.LostAbilityCards.Contains(card)
                    && !actor.CharacterClass.PermanentlyLostAbilityCards.Contains(card))))
            {
                DropClaim(b.ClaimId);
                b.Active = false;
                b.HasFace = false;
                b.Art?.HideFront();
                SetFrontFace(b, showsBack: true);
                b.Go.SetActive(false);
                continue;
            }
            RefreshFaceVisibility(b);
            if (b.HandoverLogged && !CanDrawBurn(b))
            {
                b.Go.SetActive(false);
                b.ArtworkWait += step;
                if (b.ArtworkWait >= 2f)
                {
                    DropClaim(b.ClaimId);
                    b.Active = false;
                    b.Art?.HideFront();
                }
                continue;
            }
            b.Elapsed += step;

            // A stationary card follows its recess. Once released, VRCard.FlyToPile retains
            // its world endpoints, rotation and arch for the entire flight.
            if (!b.HandoverLogged && OwnsDisplayedBoard(b))
                CaptureFlightPose(b);

            if (!b.HandoverLogged)
            {
                // The MODEL names the burn, but only the owner's semantic release event names
                // the start of its flight. The receiver's hidden game widget does not run the
                // owner's native timeline: MB482 LeapingCleave proved 0.51s here vs 1.99s there.
                // The recess still decides which renderer owns the stationary card. A known back
                // is explicitly hidden before this slab turns its burning front on (report 5b).
                bool recessDraws = OwnsDisplayedBoard(b) && b.CardId != int.MinValue
                                   && _owner.RecessShowingCard(b.CardId) == b.Recess
                                   && b.Recess >= 0;
                CardEffects? ownerFx = BurnArtwork.EffectsOf(b.Widget);
                bool playing = BurnArtwork.Playing(ownerFx);
                if (playing)
                    b.ArtworkObserved = true;
                // A missing unreliable release can use the owner's EMPTY recess as fallback,
                // after the native maximum hold. Never fly from a slot still occupied on its
                // owner's board merely because this client's UI did not run the native effect.
                bool release = b.OwnerReleased && (b.CompletionTime < 0f
                    || CardAppearanceMirror.HasPresentedThrough(b.PresentationPlayer, actor, card, b.CompletionTime));
                if (!release)
                {
                    // WHO DRAWS THE CARD DURING THE HOLD. While the owner's recess is still drawing
                    // it, this slab must stay hidden — RemoteBoardCard.DriveUsedCardFx is ramping
                    // the very same face in that seat and two copies of one card is a worse
                    // divergence than the one this class fixes.
                    //
                    // BUT WHEN NOBODY IS DRAWING IT, THIS SLAB MUST. "Das soll so synchron mit den
                    // anderen Spielern sein (also die sehen auch dass die Karte kurz liegen bleibt
                    // und erst dann kommt die Animation)" is this round's ruling and it is explicit:
                    // the peer has to SEE the card lie still. A short-rest sacrifice is precisely
                    // the case with no recess to draw it — SpareDagger above — and hiding the slab
                    // there showed the viewer nothing at all for the owner's whole hold.
                    //
                    // THIS DOES NOT RE-BREAK THE PREVIOUS ROUND'S RULING ("Ich will aber gar nicht
                    // sehen, wie die Karte in mini auf dem Board liegt … das soll unmittelbar nach
                    // dem Verschwinden geschehen"). That one is about a slab left standing AFTER the
                    // owner's card had already gone. Here the owner has NOT let go — his own hold is
                    // still running, on the same expression, on this same frame. The slab is shown
                    // only while he is showing his, and never for a moment longer.
                    bool showSlab = !recessDraws && CanDrawBurn(b);
                    if (showSlab && OwnsDisplayedBoard(b))
                    {
                        if (b.FromActive) _owner.SuppressBurnActiveCard(b.CardId);
                        else _owner.SuppressBurnRecess(b.Recess);
                    }
                    if (b.Go.activeSelf != showSlab)
                        b.Go.SetActive(showSlab);
                    if (recessDraws)
                        b.SeatedFrames++;
                    else
                    {
                        b.StationaryShown += step;
                        b.Go.transform.SetPositionAndRotation(b.From, DrawnBoardRotation);
                        // …AND ITS SIZE, PER FRAME, for the same reason the arc's is: the
                        // owner can be zooming their board while their card lies there,
                        // and this slab is not parented to the board root. It was written
                        // once at Present and then held for the whole hold.
                        b.Go.transform.localScale = Vector3.one
                            * (DrawnBoardScale * (b.FromWidth / RemoteHandFan.DefaultCardWidth));
                        // ─── THE RAMP, NOT ITS END STATE (2026-09-07 review R2, F5) ────────────
                        // This wrote SetAbilityBurnProgress(1f) — the SETTLED char — on the slab's
                        // first drawn frame and every frame after, while the owner's card chars over
                        // CardEffects.BurnCardTimeline's hard-coded `burnTime = 2f`
                        // (decompiled/GH.Runtime/CardEffects.cs:515, ramping
                        // `_GreyOut = Clamp01(dTime)` at :564-571). Owner: a card lying still and
                        // darkening across two seconds, then flying. Viewer: a slab already fully
                        // black on frame one, standing there, then flying. Same end state, different
                        // animation — and the ruling twenty lines above ("das soll so synchron mit
                        // den anderen Spielern sein ... die sehen auch dass die Karte kurz liegen
                        // bleibt") is about exactly this window. 1:1 covers ANIMATION and TIMING.
                        //
                        // THE NUMBER WAS ALREADY IN HAND AND WAS BEING THROWN AWAY: the hand-over
                        // line below reads BurnArtwork.PaintProgress(ownerFx) purely to PRINT it.
                        // That is the timeline's own progress variable read off the widget's own
                        // material, so painting it here is not an approximation of his ramp — it is
                        // his ramp's value, on this frame.
                        //
                        // If this client cannot observe the native timeline, replay its authored
                        // two-second ramp while awaiting the owner's release. A settled 1 from
                        // frame one would skip the whole visible burn. This is a bounded visual
                        // approximation; the actual flight start still comes from the owner.
                        float charred = Mathf.Clamp01(b.Elapsed / HoldSeconds);
                        if (playing)
                        {
                            float painted = BurnArtwork.PaintProgress(ownerFx);
                            if (painted >= 0f)   // -1 is UNKNOWN and never means "unpainted"
                                charred = painted;
                        }
                        b.StationaryCharred = charred;
                        if (b.HasFace && b.Art != null && !b.Art.SetAbilityBurnProgress(charred))
                            b.HasFace = false;
                    }
                    continue;
                }
                Handover(b, "the owner released this card and its native completion frame finished playback");
            }

            // THE HAND-OVER. The flight always draws the slab, and it draws it ALREADY CHARRED: a
            // fresh card lifting out of a recess the viewer just watched blacken is the one way this
            // split could read worse than the single slab it replaced. Writing the settled state
            // every frame of the flight is the same idempotent call the burnt-pile fan makes.
            bool showFlight = CanDrawBurn(b);
            if (showFlight && OwnsDisplayedBoard(b))
                _owner.TransferCardToFlight(b.FromActive ? CardFxAnchor.Active
                    : b.Recess == 0 ? CardFxAnchor.Slot0 : b.Recess == 1 ? CardFxAnchor.Slot1 : CardFxAnchor.Board,
                    b.CardId, RemoteBoardFocus.ActorById(b.ActorId), b.FlightGeneration);
            b.Go.SetActive(showFlight);
            if (b.HasFace && b.Art != null && !b.Art.SetAbilityBurnProgress(1f))
                b.HasFace = false;

            // PHASE 2 — the same over-the-board arc every other pile flight uses.
            float t = NetProtocol.CardFxSeconds > 0f
                ? Mathf.Clamp01((b.Elapsed - HoldSeconds) / NetProtocol.CardFxSeconds)
                : 1f;
            // THE OWNER'S OWN CURVE, CALLED. "The same over-the-board arc every other pile flight
            // uses" was true of the SHAPE nobody had compared and false of the path: this wrote out
            // plain smoothstep along the chord and bowed with sin(pi*t) on the RAW t, while
            // VRCard.FlyToPile eases with SMOOTHERSTEP and bows with FlyArcOffset on that EASED s.
            // See RemoteFlightCurve for the separation that produced and why no arc reading on
            // either machine could see it. WORLD up, never the owner's board up — VRCard.FlyToPile
            // deliberately throws the caller's board-up away so a tilted board can never lean the
            // arch sideways, and RemoteCardFx carries that sentence verbatim. Same rule here.
            float e = RemoteFlightCurve.Ease(t);
            Vector3 p = RemoteFlightCurve.Pose(e, b.From, b.To, Vector3.up, b.Arc);
            b.Go.transform.position = p;
            // Scale remains relative to the live board, matching VRCard's interpolated local
            // scale under its parent. Its captured world-space flight position stays independent.
            b.Go.transform.localScale = Vector3.one
                * (DrawnBoardScale * (Mathf.Lerp(b.FromWidth, b.ToWidth, e) / RemoteHandFan.DefaultCardWidth));
            if (t >= 1f)
            {
                b.Active = false;
                b.Art?.HideFront();
                b.HasFace = false;
                // A parked slab with no print on it wears the card BACK again — the state every
                // path that tears a front down has to leave behind, so a pooled slab can never be
                // re-shown with an edge ring around a bare back.
                SetFrontFace(b, showsBack: true);
                b.Go.SetActive(false);
            }
        }
    }

    // ------------------------------------------------------------------ anchors + pool --

    /// <summary><c>CAbilityCard.CardInstanceID</c> of a pile widget's model card, or
    /// <see cref="int.MinValue"/> when it has none. It is the key <c>RemoteBoardCard.Set</c> stores
    /// for the card it seated, so the two surfaces are comparing the same identifier rather than two
    /// that happen to agree.</summary>
    private static int CardInstanceIdOf(AbilityCardUI? widget)
    {
        try
        {
            CAbilityCard? card = widget != null ? widget.AbilityCard : null;
            return card != null ? card.CardInstanceID : int.MinValue;
        }
        catch (System.Exception)
        {
            return int.MinValue;
        }
    }

    /// <summary>
    /// One of this peer's board anchors in world, ON THE BOARD AS IT IS DRAWN — the eased root, not
    /// the pose the packet carried.
    ///
    /// <para><c>RemoteControlBoard</c> lerps its root toward the wire pose at
    /// <c>NetProtocol.InterpolationSharpness</c> (~73 ms of trail), and
    /// <c>BoardPosition</c>/<c>BoardRotation</c>/<c>BoardScale</c> are that lerp's TARGET. A burn
    /// slab is not parented to the root, so while a peer CARRIES or ZOOMS their board — exactly
    /// when the sender raises extras to 15 Hz — the card lifted out of a recess that was no longer
    /// under it. <c>RemoteCardFx.TryResolve</c> carries the same correction and the same fallback:
    /// before this peer's first pose lands, the raw composition, which is what every build before
    /// this one did everywhere.</para>
    /// </summary>
    private bool TryAnchor(CardFxAnchor anchor, out Vector3 world)
    {
        world = default;
        if (!_owner.HasBoard)
            return false;
        if (_owner.TryBoardAnchorWorld(anchor, out world))
            return true;
        float scale = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        world = _owner.BoardPosition + _owner.BoardRotation * (_owner.BoardAnchorLocal(anchor) * scale);
        return true;
    }

    /// <summary>This peer's board ROTATION as DRAWN, for the slab that lies on it — same
    /// raw-versus-eased argument as <see cref="TryAnchor"/>, on the rotational term.</summary>
    private Quaternion DrawnBoardRotation =>
        _owner.TryDrawnBoardPose(out _, out Quaternion rot, out _) ? rot : _owner.BoardRotation;

    /// <summary>…and its SCALE as DRAWN. During a zoom the slab was sized off a number the board
    /// underneath it had not reached yet.</summary>
    private float DrawnBoardScale =>
        _owner.TryDrawnBoardPose(out _, out _, out float s) && s > 0f
            ? s
            : (_owner.BoardScale > 0f ? _owner.BoardScale : 1f);

    /// <summary>Tell this burn slab's card BODY what its FRONT fan wears — see
    /// <c>CardMesh.SetBodyFrontFace</c>, which owns the rule and the (fade-aware) write.
    ///
    /// <para>EDGE-GATED, like every other caller: a steady burn never walks CardMesh's body
    /// registry. The gate lives on the Burn because up to <see cref="MaxBurns"/> presentations run
    /// at once and a damage burn commits two cards in the same frame, one of which may resolve a
    /// front while the other does not.</para>
    ///
    /// <para>MID-FADE: this class is a <c>PeerBoardFade</c> follower (<c>Always</c>) and its slab
    /// hangs over the owner's board for the whole burn, so a ramp really can be running underneath
    /// it. The write therefore goes through <c>PeerBoardFade.SetSubmeshMaterial</c>, which edits the
    /// remembered authored array and the installed clone for slot 0 only. That path is argued from
    /// <c>Swap</c>/<c>Restore</c>/<c>SettleDepthState</c> and is NOT hardware-verified.</para></summary>
    private static void SetFrontFace(Burn b, bool showsBack)
    {
        if (b.Go == null || b.WearsBack == showsBack)
            return;
        CardMesh.SetBodyFrontFace(b.Go.transform, showsBack);
        b.WearsBack = showsBack;
    }

    private Burn Acquire()
    {
        for (int i = 0; i < _burns.Count; i++)
        {
            if (!_burns[i].Active)
                return _burns[i];
        }
        if (_burns.Count >= MaxBurns)
        {
            Burn oldest = _burns[0];
            for (int i = 1; i < _burns.Count; i++)
            {
                if (_burns[i].Elapsed > oldest.Elapsed)
                    oldest = _burns[i];
            }
            // AN ABANDONED PRESENTATION MUST GIVE ITS TOKEN BACK, and only if it never flew
            // (2026-09-07 review R3, F5). This slab is being taken away from a burn that is still
            // mid-hold, so this viewer never sees that burn's arc — and swallowing the owner's
            // event for it would delete the fallback back-slab flight RemoteCardFx would otherwise
            // draw, leaving the burn with NO animation on this client at all. A presentation that
            // had already handed over KEEPS its token: the arc was drawn, and letting the event
            // through would fly the same burn a second time, which is the defect the swallow
            // exists to prevent.
            if (!oldest.HandoverLogged)
                DropClaim(oldest.ClaimId);
            oldest.ClaimId = 0;
            oldest.Art?.HideFront();
            oldest.HasFace = false;
            SetFrontFace(oldest, showsBack: true);   // …the same teardown, one recycle earlier
            return oldest;
        }

        EnsureRoot();
        var b = new Burn();
        if (_root != null)
        {
            var go = new GameObject($"Burn{_burns.Count}");
            go.transform.SetParent(_root.transform, worldPositionStays: false);
            float w = RemoteHandFan.DefaultCardWidth;
            float h = RemoteHandFan.DefaultCardHeight;
            // THE BODY IS SIZED TO THE FACE IT WILL WEAR, one level down, exactly as
            // RemoteHandFan builds its slabs (2026-09-06 report item 5, the third instance of one
            // defect). The flight slab used to carry the mesh on the ROOT at the full nominal
            // 63.5 x 88 mm while RemoteCardArt letterboxed the 294 x 450 px print inside it, so a
            // burning card flew across the table with 17.5 % more body than print at the sides.
            // The root STAYS uniform — Fly() writes its localScale and the print's world-space
            // canvas hangs off it, so a non-uniform scale here would stretch the art.
            var body = new GameObject("Body");
            body.transform.SetParent(go.transform, worldPositionStays: false);
            Vector2 vis = CardFace.VisibleFaceRect(w, h);
            body.transform.localScale = new Vector3(vis.x / w, vis.y / h, 1f);
            var filter = body.AddComponent<MeshFilter>();
            // The owner's punched-out ABILITY body, out of CardMesh's shared cache (never ours to
            // destroy) — the same body every other mirrored card slab wears.
            CardMesh.AttachBody(filter, CardBodyKind.Ability, w, h);
            var renderer = body.AddComponent<MeshRenderer>();
            Material back = CardMesh.CreateBackMaterial(CardBodyKind.Ability);
            renderer.sharedMaterials = new[] { back, back };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            VRLayers.Apply(go);
            b.Go = go;
            b.Art = new RemoteCardArt(go.transform, w, h)
            {
                // Names this face for the card-FX instrument, which latches PER SURFACE so a
                // term that arms on the recess and refuses on the flight is readable rather
                // than silent. See RemoteCardArt.FxSurface.
                Surface = RemoteCardArt.FxSurface.Flight,
            };
        }
        _burns.Add(b);
        return b;
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;
        _root = new GameObject($"GloomhavenVR.RemoteBurnFx[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _root.transform.localScale = Vector3.one;
        // Same board fade every other mirrored surface follows (user item 7, 2026-09-02): a card
        // burning at full opacity over a board that is faded out is the wrongness he described.
        PeerBoardFade.Follow(_owner.PlayerId, _root.transform);
        VRLayers.Apply(_root);
    }

    internal void Destroy()
    {
        for (int i = 0; i < _burns.Count; i++)
            _burns[i].Art?.Destroy();
        _burns.Clear();
        _known.Clear();
        _burntBuf.Clear();
        _pruneScratch.Clear();
        _seeded = false;
        _claimTokens.Clear();
        _pendingReleases.Clear();
        _shortRestCandidate = null;
        _shortRestActor = 0;
        _shortRestRound = 0;
        if (_root != null)
        {
            // The body meshes are CardMesh's SHARED cache — never ours to destroy.
            Object.Destroy(_root);
            _root = null;
        }
    }

    // ------------------------------------------------------------------ instruments --

    /// <summary>
    /// HARDWARE VERIFICATION (2026-09-05 item 11a): the RECEIVER's answer to "which card was burned". Grep
    /// token <c>BURN CARD</c> — deliberately the SAME token the owner's
    /// <c>CardsDriver.LogBurnAttribution</c> prints, so one grep across the two hardware logs
    /// decides the 1:1 question with no arithmetic: for each burn the owner's line and this line
    /// must name the same card.
    ///
    /// <para>Event-driven (a handful per scenario), never per frame.</para>
    ///
    /// <para>PROOF the fix landed: a <c>[peer]</c> line naming the SAME card as the owner's
    /// <c>[owner/…]</c> line for that burn, with <c>face=REAL</c>.</para>
    ///
    /// <para>FALSIFIERS, and they say different things. (1) The two lines naming DIFFERENT cards:
    /// the identity resolve is wrong and 11a is NOT fixed — the next lead is
    /// <c>RemotePileFronts.Resolve</c>, which walks the same list. (2) No <c>[peer]</c> line at all
    /// beside an owner line: this mirror never armed, so the peer still gets the old back slab
    /// through <see cref="RemoteCardFx"/> — read the <c>BURN MIRROR SKIPPED</c> line for the
    /// reason. (3) The right card with <c>face=BACK</c>: the reveal gate or the clone refused, and
    /// only the FACE half of the fix is inert; the flight and the timing are still right.</para>
    /// </summary>
    private void LogAttribution(string name, bool fronts, bool face, CPlayerActor? actor, int recess,
                                string recessHow)
    {
        string where = recess >= 0
            ? $"in their round recess {recess + 1} ({recessHow}) — so this presentation holds and "
              + "flies from the seat their own card is lying in, not from the board centre"
            : $"at their board CENTRE, because {recessHow}; that is the pre-2026-09-06 picture and "
              + "the fallback, not the intent — it is report item 7's symptom and any occurrence "
              + "of it is a finding";
        // HW-VERIFY: grep token "BURN CARD" — the same token the OWNER's
        // CardsDriver.LogBurnAttribution prints, so one grep across the two hardware logs decides
        // the 1:1 question. See this method's doc for the three falsifiers.
        VRLog.Note("Net", $"BURN CARD [peer {_owner.PlayerId}]: that player burned '{name}' — " +
                          $"showing it {where}, charring there for as long as the OWNER's own burn " +
                          $"artwork holds his card (BurnArtwork.Released, his expression: grace " +
                          $"{BurnArtwork.StartGraceSeconds:F2}s / deadline " +
                          $"{BurnArtwork.MaxHoldSeconds:F1}s), then flying " +
                          $"it into their Burnt stack ({NetProtocol.CardFxSeconds:F2}s). " +
                          $"face={(face ? "REAL" : "BACK")}, " +
                          $"RevealGate.CardFaces={(fronts ? "open" : "shut")} (the CARD-aware " +
                          "overload, which carries the burn exception — NOT ShowRoundCardFronts, " +
                          "so 'shut' here is never by itself a statement about the phase), " +
                          $"char='{Board.CharacterFocus.Describe(actor)}', burn #{_played}. TIMING: " +
                          $"found on the burnt-pile walk at frame {_lastWalkFrame}, " +
                          (_sinceLastWalk < 0f
                              ? "the FIRST walk for this character (no bound on the wait)"
                              : $"{_sinceLastWalk * 1000f:F0} ms after the previous walk — this " +
                                "mirror POLLS the host-replicated Lost pile at " +
                                $"{WatchSeconds * 1000f:F0} ms, so that number " +
                                "IS the upper bound on the delay report item 7 names; anything much " +
                                "larger than the cadence is a stalled board pass and not the poll") +
                          ", then the hold, which ENDS WHEN THEIR RECESS DOES — read the 'BURN " +
                          "FLIGHT' line beside this one for when the arc actually began. The identity " +
                          "was read from THIS client's own copy of that character's host-replicated " +
                          "LostAbilityCards list (the same CardsGameApi.GetPileWidgets call the mirrored " +
                          "burnt-pile fan uses) — NO card identity crossed the wire and no wire field " +
                          "was added. Compare with the owner's 'BURN CARD [owner/...]' line for the " +
                          "same burn: they must name the same card.");
    }

    private void CaptureFlightPose(Burn b)
    {
        CardFxAnchor origin = b.Recess == 0 ? CardFxAnchor.Slot0
            : b.Recess == 1 ? CardFxAnchor.Slot1 : CardFxAnchor.Board;
        if (b.FromActive && _owner.TryDrawnBoardPose(out Vector3 bp, out Quaternion br, out float bs))
            b.From = bp + br * (b.ActiveLocal * bs);
        else if (TryAnchor(origin, out Vector3 from)) b.From = from;
        if (TryAnchor(CardFxAnchor.Burnt, out Vector3 to)) b.To = to;
        float height = Mathf.Max(0.01f, _owner.BoardTuning.CardWidth) * (88f / 63.5f);
        b.Arc = Mathf.Max(height * 1.5f * DrawnBoardScale,
            Vector3.Distance(b.From, b.To) * VRCard.FlyArcHeightFraction);
        if (b.Go != null) b.Go.transform.rotation = DrawnBoardRotation;
    }

    /// <summary>
    /// THE HAND-OVER, AS MECHANISM: this presentation stops holding and its arc begins now.
    ///
    /// <para>Every load-bearing write of the transition lives here and none of it in
    /// <see cref="LogHandover"/>. <see cref="ReleaseClaim"/> in particular starts this
    /// presentation's swallow window, which is what <see cref="ConsumesWireEvent"/> reads to decide
    /// whether the owner's <c>Board -&gt; Burnt</c> wire event is swallowed or flies a second time,
    /// so it is the mechanism's state and not a diagnostic's.</para>
    ///
    /// <para>THE CLOCK IS RE-BASED, NOT CARRIED. Phase 2 reads
    /// <c>(Elapsed - HoldSeconds) / CardFxSeconds</c>, and since the hold now lasts as long as the
    /// owner's recess draws the card, a hold that spanned a whole turn would enter the arc at
    /// t &gt;= 1 — the card would appear in the stack without ever being seen to fly, which is the
    /// exact teleport this class exists to remove.</para>
    /// </summary>
    private void Handover(Burn b, string why)
    {
        if (b.HandoverLogged)
            return;
        float after = b.Elapsed;
        if (OwnsDisplayedBoard(b)) CaptureFlightPose(b);
        b.HandoverLogged = true;
        b.FlightGeneration = _owner.NextFlightOwnership();
        // THIS TOKEN'S OWN WINDOW STARTS HERE. The owner reports his '-> Burnt' event when his card
        // is really cleared, which is the signal this hand-over just read off his widget, so the
        // slack after this instant is all the swallow needs — and it is this token's slack, not the
        // whole set's.
        ReleaseClaim(b.ClaimId);
        b.Elapsed = HoldSeconds;
        Cards.CardFlightLedger.Note($"peer {_owner.PlayerId}", "Burnt", "remote-burn-mirror", b.Name);
        LogHandover(b, after, why);
    }

    /// <summary>
    /// HARDWARE VERIFICATION (2026-09-06 late report, item 9): the instant the mirrored burn stops
    /// being the owner's seated card and becomes a flight, and how long — if at all — a STATIONARY
    /// slab was visible before it. Grep token <c>BURN FLIGHT</c>. One line per burn, at the
    /// hand-over, never per frame.
    ///
    /// <para>WHY IT IS A SECOND LINE AND NOT A CLAUSE ON <see cref="LogAttribution"/>. That line is
    /// printed when the burn is DISCOVERED, and everything item 9 asks about happens afterwards: the
    /// hold's real length is not known until it ends, and the whole question is whether a slab was
    /// ever shown standing still. A report about a discovery cannot answer a question about a
    /// hand-over.</para>
    ///
    /// <para>AND SINCE ModBuild 475 IT IS ALSO THE 1:1 CLAIM ITSELF (item 8). The hold no longer
    /// runs on the recess proxy; it runs on <see cref="BurnArtwork.Released"/>, the owner's own
    /// expression over the owner's own widget. That makes the owner-vs-mirror gap a number this line
    /// can be CHECKED against instead of one it has to assert: <c>held=X.XXs</c> here and
    /// <c>BURN HOLD: … waited X.XXs</c> for the SAME card name in the other client's log must agree
    /// to within the 0.08 s watch cadence. The report is the pair, not this line alone.</para>
    ///
    /// <para><b>WORKING</b> = one line per burn whose <c>held=</c> figure matches the same card's
    /// owner-side <c>BURN HOLD</c> seconds to within 0.10 s, with <c>artwork=observed</c> on a
    /// client that is presenting that character's 2D UI and <c>artwork=not-observable</c> on one
    /// that is not. For the three burns of the ModBuild 474 session that means 0.67 / 0.50 / 2.01 s
    /// against the pre-fix 1.00 / 0.02 / 0.56 s.</para>
    ///
    /// <para><b>INERT</b> = <c>held=0.00s</c>, or any <c>held=</c> still differing from the owner's
    /// <c>BURN HOLD</c> by more than 0.10 s. Zero in particular means <see cref="Burn.Widget"/> came
    /// back null and the whole predicate degenerated — the lead is then <c>Watch()</c>'s widget, not
    /// the hold. A <c>released by: their recess</c> clause anywhere is the pre-fix build.</para>
    ///
    /// <para>THE FOURTH ARM IS NOT A DEFECT AND MUST NOT BE READ AS ONE. <c>released by: the
    /// OWNER's presented character changed</c> is the focus flush, and the owner-side pair for it
    /// is his <c>BURN ANIM: FLUSHING the held flight of …</c> line for the same card, not a
    /// <c>BURN HOLD</c> with a matching <c>held=</c>. If that flush line is absent, this mirror
    /// flushed on the reveal-gate's fallback edge instead (RemoteBoardFocus rule 1) and the owner
    /// kept holding — bounded by his 3 s deadline, and the one case in the burn flow where the two
    /// sides are knowingly not on one term.</para>
    ///
    /// <para><b>STILL BEYOND THE INSTRUMENT</b> = matched <c>held=</c> figures on both clients and
    /// the user still reporting that the card does not lie still. The hold would then be right and
    /// the DRAWING wrong, and the next surfaces are the recess mirror (<c>RemoteBoardCard</c>, which
    /// draws the card for <c>seated=N frame(s)</c> of the hold) and this class's own slab (which
    /// draws it for <c>stationary=X.XXs</c>): those two must together cover the whole of
    /// <c>held=</c>, and this line prints all three so the arithmetic can be done from the log.</para>
    /// </summary>
    private void LogHandover(Burn b, float after, string why)
    {
        // NOTHING LOAD-BEARING IS WRITTEN HERE, DELIBERATELY. The hand-over's bookkeeping —
        // `HandoverLogged`, the token's release stamp, the arc's re-based clock — all lives in
        // <see cref="Handover"/>, because that stamp is READ by ConsumesWireEvent to decide
        // whether a peer's burn flies once or twice. A state write inside a logger is the shape
        // that once nearly latched the wall fade off forever when a spent log line was deleted;
        // check-instrument-writes.py caught this one on its first draft. This method may be gated
        // off, retired or demoted and the behaviour is identical.
        // HW-VERIFY: grep token "BURN FLIGHT" — see this method's doc for the three readings.
        VRLog.Note("Net", $"BURN FLIGHT [peer {_owner.PlayerId}]: '{b.Name}' leaves for their Burnt " +
                          $"stack now. held={after:F2}s since this mirror discovered the burn, " +
                          $"signal={(b.OwnerReleased ? "owner release event" : "missing-event fallback")}, " +
                          $"artwork={(b.ArtworkObserved ? "observed" : "not-observable")}, " +
                          $"released by: {why}. DURING THE HOLD the card was drawn by their recess " +
                          $"{(b.Recess >= 0 ? (b.Recess + 1).ToString() : "(none — board centre)")} " +
                          $"for seated={b.SeatedFrames} frame(s) and by this class's own slab for " +
                          $"stationary={b.StationaryShown:F2}s at char=" +
                          (b.StationaryCharred >= 0f ? b.StationaryCharred.ToString("F2") : "n/a") +
                          " of 1.00 (the OWNER's own CardEffects._GreyOut, read off his widget on " +
                          "this machine: a stationary hold that ENDS near 1.00 is his 2 s ramp " +
                          "mirrored frame for frame, one pinned at 1.00 from the first frame is the " +
                          "not-observable arm keeping the settled char); those two must cover the whole of " +
                          "held= or the peer saw nothing lying there. THE 1:1 CLAIM IS THE PAIR, " +
                          "NOT THIS LINE: grep the OWNER's 'BURN HOLD' for this same card name in " +
                          "the other client's log — 'waited X.XXs' must equal held= to within the " +
                          $"{WatchSeconds:F2}s watch cadence, because both clients now evaluate ONE " +
                          "expression over ONE widget (the model is local; the owner's " +
                          "AbilityCardUI is a real object on this machine). Any other reading is a " +
                          "1:1 breach with a number on it. ModBuild 474, the build this replaced, " +
                          "read 1.00/0.02/0.56s against owner holds of 0.67/0.50/2.01s.");
        // HW-VERIFY: grep token "BURN FLIGHT CURVE" — the SHAPE and the SIZE of the arc this
        // hand-over starts, the pair no burn line has ever printed. Every arc reading here was the
        // peak height, which is identical under any symmetric ease, so it could never have shown
        // that this class flew a different curve from its owner; and the slab's scale was written
        // ONCE, so a burn leaving a recess arrived there at the wrong card size with no line saying
        // so. One line per burn, beside the BURN FLIGHT line it belongs to.
        VRLog.Note("Net", $"BURN FLIGHT CURVE [peer {_owner.PlayerId}]: '{b.Name}'. "
                          + $"{RemoteFlightCurve.Describe(b.Arc)} SIZE ramps on the same eased term: "
                          + $"{b.FromWidth * 1000f:F1} mm -> {b.ToWidth * 1000f:F1} mm "
                          + $"({(b.Recess >= 0 ? "a RECESS origin, so the origin width is the owner's "
                                                 + "own SlotCardWidth and the slab can no longer be a "
                                                 + "second card size inside their recess card"
                                               : "no recess origin, so both ends are their hand CardWidth")}).");
    }

    /// <summary>
    /// HARDWARE VERIFICATION: why a burn this client KNOWS about was not presented. Without it, "the peer saw
    /// nothing" and "the peer saw the old back slab" are indistinguishable in a log. One line per
    /// skipped burn, event-driven.
    /// </summary>
    private void LogSkipped(string name, string reason)
    {
        // HW-VERIFY: grep token "BURN MIRROR SKIPPED" — it is what separates "the peer saw nothing"
        // from "the peer saw the old anonymous back slab", which a log otherwise cannot tell apart.
        VRLog.Note("Net", $"BURN MIRROR SKIPPED [player {_owner.PlayerId}]: '{name}' burned, but " +
                          $"{reason}. The owner's own Board→Burnt card-FX event is therefore NOT " +
                          "claimed and still plays as the anonymous card-back slab (RemoteCardFx), " +
                          "which is exactly the picture every build before this one showed.");
    }
}
