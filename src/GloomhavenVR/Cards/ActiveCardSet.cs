using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// WHICH CARDS ARE ACTIVE, FOR ONE CHARACTER — the single authoritative answer, plus the census
/// that proves every client agrees with every other one.
///
/// <para>USER ITEM 8b (2026-09-06, verbatim): "Jederzeit muss synchron bleiben welche Karte aktiv
/// ist, und jeder das selbe sehen und das unmittelbar: Sobald eine Karte aktiviert wurde, sollen
/// allen Spieler die den character auf ihrem board ausgewählt haben die aktive Karte sehen und AUCH
/// auf dem remote board entsprechend sichtbar."</para>
///
/// <para>ROOT CAUSE THIS FILE EXISTS — TWO SOURCES FOR ONE FACT, AND ONE OF THEM IS A PRESENTATION
/// CACHE. Before this file the mod answered "is this card active?" in two different ways on the
/// SAME machine for the SAME actor:</para>
/// <list type="bullet">
/// <item><description>the peer's mirror (<c>Net.RemoteActiveCards</c>) read the MODEL —
/// <c>CCharacterClass.ActivatedCards</c>, the list the rules themselves move a card into;
/// </description></item>
/// <item><description>the owner's OWN column (<c>ActivePileViewer</c>, fed by
/// <c>CardsGameApi.GetActivePileWidgets</c>) read the WIDGET — <c>AbilityCardUI.CardType ==
/// CardPileType.Active</c>.</description></item>
/// </list>
/// <para>Those are not two readings of one fact. <c>AbilityCardUI.cardType</c> is a CACHE, written
/// in exactly one gameplay path — <c>CardsHandUI.UpdateCard</c> (CardsHandUI.cs:1433-1439) via
/// <c>UpdateCards</c>, reached only when the game's own 2D hand view is refreshed
/// (<c>UpdateView</c>/<c>SetMode</c>). The other two <c>SetType(CardPileType.Active)</c> call sites
/// in the whole game are <c>AbilityCardUI.EditorInit</c> and <c>CreateCardsInit</c>, neither of
/// which runs in a scenario. So between "the rules activated the card" and "the game happened to
/// re-run UpdateCards on that hand" the widget still says <c>Hand</c> or <c>Round</c> — and the
/// owner's own board, which is the one surface that MUST be first, was last.</para>
///
/// <para>THE USER'S REPORT IS THAT DIVERGENCE, MEASURED FROM THREE SEATS AT ONCE: the observer's
/// remote board (model → correct and immediate), the owner's own board (widget cache → late), and a
/// third seat with that character selected (widget cache that is NEVER re-stamped at all, because
/// the game only ever drives the LOCAL player's hand view — plus a turn gate that no longer exists,
/// see the next paragraph). No wire field could have fixed any of it: the fact was already on all
/// three machines. It was read from the wrong place on two of them.</para>
///
/// <para>SO EVERY SURFACE ASKS HERE NOW, and the answer is the model, always. Sibling lanes that
/// need "is this card currently active" — the burn-presentation trigger (activated-but-not-yet-lost
/// must not look or sound burnt) and the face policy (an active card taken into the hand shows its
/// FRONT even in the selection phase) — call <see cref="IsActive(CPlayerActor, CBaseCard)"/> and
/// get the same answer this column draws, by construction rather than by agreement.</para>
///
/// <para>AND ONE SOURCE WAS NOT ENOUGH — USER ITEM 4 (2026-09-07, verbatim): "In meinem Zug habe
/// ich eine Karte aktiviert. Sie wurde bei mir NICHT sofort lokal angezeigt. Allerdings sieht mein
/// Mitspieler sie bei mir … Die aktive Karte ist lokal mal aufgetaucht und wieder verschwunden als
/// ein anderer Spieler dran war. Auch hier hat das remote board sie richtig angezeigt." Making both
/// surfaces read this file fixed WHAT they believed and left untouched WHETHER they drew it:
/// <c>CardsDriver.UpdateActive</c> still opened with a turn gate that hid the owner's whole column
/// on every turn that was not the presented character's, while <c>Net.RemoteActiveCards</c> has
/// never had one. Same fact, same machine, same frame, one gate — ten disappearances of one card in
/// a single ModBuild 470 session. The gate is now deleted; requirement #5, which it was a
/// misapplication of, keeps its real choke point in <c>Board.CharacterFocus.RoundCardDock</c> and
/// governs the two PLAYED round cards, which belong to one turn. An ACTIVE card by definition
/// outlives the turn that played it, so no turn-scoped term may ever decide whether it is drawn.
/// </para>
///
/// <para>THE CENSUS BELOW MISSED ALL OF THAT AND SAID SO IN NUMBERS, which is why it now has a
/// <see cref="Belief.Model"/> row. Both of its rows used to be DRAWN pictures, so it could only ask
/// "do my two boards agree with each other" and it excused a SUPPRESSED seat as deliberate — the
/// exact shape of this defect. It printed 0 DISAGREE on 155 host and 146 peer lines of the session
/// the user reported. A row that is not a picture of anything is the one row that cannot be fooled
/// by every picture being wrong the same way.</para>
///
/// <para>ALLOCATION: never <c>CCharacterClass.ActivatedAbilityCards</c>. That property is a LINQ
/// projection that materialises a NEW list on every read (CCharacterClass.cs:99) and these are
/// per-rebuild paths. <see cref="ActivatedCards"/> hands back the raw
/// <c>List&lt;CBaseCard&gt;</c> field instead.</para>
/// </summary>
internal static class ActiveCardSet
{
    // ============================================================== the authoritative answer ==

    /// <summary>
    /// The RAW activated-card list of a character, or null. This is
    /// <c>CCharacterClass.ActivatedCards</c> — the <c>List&lt;CBaseCard&gt;</c> field itself
    /// (CCharacterClass.cs:97), NOT the allocating <c>ActivatedAbilityCards</c> projection beside
    /// it. Never mutated here: this whole file is read-only over game state.
    /// </summary>
    internal static List<CBaseCard>? ActivatedCards(CCharacterClass? klass)
    {
        if (klass == null)
            return null;
        try { return klass.ActivatedCards; }
        catch { return null; }
    }

    /// <inheritdoc cref="ActivatedCards(CCharacterClass?)"/>
    internal static List<CBaseCard>? ActivatedCards(CPlayerActor? actor)
    {
        if (actor == null)
            return null;
        try { return ActivatedCards(actor.CharacterClass); }
        catch { return null; }
    }

    /// <summary>
    /// THE authoritative "is this card active right now" for one character. Model-sourced, so it is
    /// true on the frame THE RULES moved the card and on every client that has the actor — not on
    /// the frame the game next refreshes a 2D hand view.
    ///
    /// <para>WHICH FRAME THAT IS, MEASURED IN THE DECOMPILED SOURCE RATHER THAN ASSERTED — and the
    /// sentence that used to stand here was wrong. It read "true on the frame
    /// <c>CCharacterClass.ActivateCard</c> ran", and named CCharacterClass.cs:362 as the append an
    /// ABILITY card takes "at the instant of activation". <c>ActivateCard</c> has exactly two call
    /// sites in the whole game (CActiveBonus.cs:401 and :406) and both sit inside a branch guarded
    /// by <c>baseCard.CardType</c> being an ITEM, an ATTACK MODIFIER or an enemy AURA — an ability
    /// card can never reach it. An ability card enters <c>m_ActivatedCards</c> at
    /// CCharacterClass.cs:467, the <c>ECardPile.Activated</c> branch of
    /// <c>MoveAbilityCardToPile</c>, which is reached from <c>DiscardRoundAbilityCard</c> — the
    /// END-OF-TURN drain. That is not a quibble: it is the difference between "the mirror may draw
    /// an active card the moment its bonus starts" and "the mirror must draw it when the owner's
    /// board does", which is user item 2 of 2026-09-07. BOTH LOGS OF THE ModBuild 476 SESSION AGREE
    /// WITH THE CORRECTED READING and not with the old comment: the peer's own MODEL row for
    /// 'BruteID' moved to [7:ABILITY_CARD_WardingStrength] at their frame 36252, in the same block
    /// as <c>End of ability syncing finished</c>, and the host's row for the same character moved
    /// at host frame 78590, in ITS copy of that block.</para>
    ///
    /// <para>The game's piles are mutually exclusive by construction (its own duplicate check,
    /// CCharacterClass.cs:318-350, logs an error if a card is in two), so a TRUE here also means the
    /// card is not in hand, not discarded and not lost.</para>
    /// </summary>
    internal static bool IsActive(CCharacterClass? klass, CBaseCard? card)
    {
        if (card == null)
            return false;
        List<CBaseCard>? list = ActivatedCards(klass);
        if (list == null)
            return false;
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], card))
                return true;
        return false;
    }

    /// <inheritdoc cref="IsActive(CCharacterClass?, CBaseCard?)"/>
    internal static bool IsActive(CPlayerActor? actor, CBaseCard? card) =>
        actor != null && IsActive(SafeClass(actor), card);

    /// <summary>
    /// WHICH HALF (or halves) of an active card is the one that is ACTUALLY ACTIVE — the region the
    /// owner's own board pulses.
    ///
    /// <para>USER ITEM 3 (2026-09-07, verbatim): "Der Spieler sieht bei den aktiven Karten
    /// pulsierend den Bereich der aktiv ist - das ist aber nicht der Fall beim remote board. Dort
    /// soll das auch entsprechend synchronisiert angezeigt werden - auch wenn der Spieler die
    /// jeweilige aktive Karte in die Hand nimmt soll das pulsieren sichtbar sein."</para>
    ///
    /// <para>IT NEEDS NO WIRE FIELD AND THAT IS THE WHOLE POINT. The answer is a pure function of
    /// <c>CCharacterClass.FindCasterActiveBonuses</c> and <c>CAbilityCard.GetAbilityActionType</c>
    /// — two reads of the rules model, which every client simulates for every actor. So the
    /// observer's mirror can resolve the SAME half from the SAME objects the owner's board reads,
    /// in the same frame, and a wire field would only be a second, lossier copy of a fact that is
    /// already on the machine. This is the same argument that made the whole of this file
    /// model-sourced; the highlight is simply the last surface that had not asked.</para>
    ///
    /// <para>BOTH TRUE IS A REAL ANSWER, NOT A FAILURE. A whole-card or unresolvable bonus lights
    /// the WHOLE card on the owner's board, and the fallback at the bottom says so explicitly: an
    /// active card with no nameable half is drawn fully lit rather than dark, because "this card is
    /// doing something" is the fact the pulse carries.</para>
    ///
    /// <para>ONE EXPRESSION, TWO CALLERS. <c>CardsGameApi</c>'s
    /// <c>GetActiveHalves(CardsHandUI, CAbilityCard, out, out)</c> is the OWNER's entry and, since
    /// the 2026-09 refactor, delegates here (it takes a hand only to reach <c>hand.PlayerActor</c>,
    /// the single argument this one takes directly). Until then it was a second, term-for-term
    /// copy that the two boards had to keep agreeing on by hand; now the owner's pulse and the
    /// mirror's are literally the same expression.
    /// </para>
    /// </summary>
    internal static void ActiveHalves(CPlayerActor? actor, CAbilityCard? card,
                                      out bool top, out bool bottom)
    {
        top = false;
        bottom = false;
        if (actor == null || card == null)
            return;
        try
        {
            CCharacterClass? klass = SafeClass(actor);
            if (klass == null)
                return;
            List<CActiveBonus> bonuses = klass.FindCasterActiveBonuses(actor);
            for (int i = 0; i < bonuses.Count; i++)
            {
                CActiveBonus bonus = bonuses[i];
                if (bonus == null || !ReferenceEquals(bonus.BaseCard, card))
                    continue;
                CBaseCard.ActionType type = card.GetAbilityActionType(bonus.Ability);
                if (type == CBaseCard.ActionType.TopAction)
                    top = true;
                else if (type == CBaseCard.ActionType.BottomAction)
                    bottom = true;
                else
                {
                    top = true;   // whole-card / NA bonus → the whole card is the active region
                    bottom = true;
                }
            }
        }
        catch
        {
            top = false;
            bottom = false;
        }
        if (!top && !bottom)   // active card with no resolvable half → the whole card, as the owner
        {
            top = true;
            bottom = true;
        }
    }

    /// <summary>
    /// Change-gate hash of one character's ACTIVE SET, model-sourced and allocation-free.
    ///
    /// <para>THE WATCHDOG HAD TO MOVE TOO, and that is half the "unmittelbar" defect. The old
    /// <c>CardsDriver.ActiveSignature</c> hashed <c>GetActivePileWidgets</c> — i.e. the widget CACHE
    /// — so it could not fire until the very thing it was supposed to detect had already been
    /// mirrored into the cache by an unrelated game refresh. A watchdog watching the cache instead
    /// of the fact does not fire late; on a hand whose 2D view the game never refreshes (any peer's,
    /// on any observer's client) it never fires at all.</para>
    ///
    /// <para>Ordering is deliberately NOT normalised: the game appends on activation, so the list
    /// order is itself a change worth rebuilding for, and two cards swapping places cannot cancel.
    /// </para>
    /// </summary>
    internal static int Signature(CCharacterClass? klass)
    {
        List<CBaseCard>? list = ActivatedCards(klass);
        if (list == null)
            return 0;
        int sig = 17;
        for (int i = 0; i < list.Count; i++)
        {
            CBaseCard? c = list[i];
            sig = sig * 31 + (c == null ? 0 : c.ID);
        }
        return sig * 31 + list.Count;
    }

    /// <inheritdoc cref="Signature(CCharacterClass?)"/>
    internal static int Signature(CPlayerActor? actor) => actor == null ? 0 : Signature(SafeClass(actor));

    /// <summary>Character class of an actor, or null — the property throws on a half-torn actor.
    /// </summary>
    private static CCharacterClass? SafeClass(CPlayerActor actor)
    {
        try { return actor.CharacterClass; }
        catch { return null; }
    }

    // ============================================================================== census ==

    /// <summary>Where a client's belief about one character's active set CAME FROM. The enumeration
    /// is the list of surfaces that can hold such a belief, so a character missing a row on one
    /// machine and present on another is itself the reading.</summary>
    internal enum Belief : byte
    {
        /// <summary>
        /// NOT A SURFACE — the MODEL, <c>CCharacterClass.ActivatedCards</c> itself, which is what
        /// every seat is supposed to be a picture of. It is stamped automatically beside every
        /// report (see <see cref="StoreModel"/>), so it costs no call site and exists for exactly
        /// the characters some seat draws.
        ///
        /// <para>ITS ABSENCE IS WHY THIS CENSUS READ CLEAN THROUGH USER ITEM 4 (2026-09-07). Both
        /// of the other rows are DRAWN pictures, so the line could only ever answer "do my two
        /// boards agree with each other". It could not answer "does either of them agree with the
        /// truth" — and the defect was precisely that one seat drew NOTHING while a card was
        /// active. With only drawn rows, that reads as a SUPPRESSED seat, which the disagreement
        /// rule deliberately excused. DISAGREE therefore printed 0 on 155 host lines and 146 peer
        /// lines of the ModBuild 470 session while the user watched the card appear and vanish ten
        /// times. A row that is not a picture of anything is the one row that could not be fooled
        /// by every picture being wrong the same way.</para>
        /// </summary>
        Model,

        /// <summary>This client's own control board drew it (<c>ActivePileViewer</c> via
        /// <c>CardsDriver.UpdateActive</c>) — for the character the board is PRESENTING, which is
        /// the local player's own or, under a read-only character focus, somebody else's.</summary>
        OwnBoard,

        /// <summary>A peer's mirrored board drew it (<c>Net.RemoteActiveCards</c>).</summary>
        RemoteBoard,
    }

    private struct Row
    {
        public string? Cards;     // sorted "id:Name" list — the comparable payload; null = never set
        public int Count;
        public int ChangedFrame;  // Time.frameCount of the last CHANGE (never of the last report)
        public float ChangedAt;   // Time.unscaledTime of the same edge
        public bool Suppressed;   // the surface ran and deliberately drew NOTHING
        public string? Why;       // why it drew nothing, when Suppressed
        public float ReportedAt;  // Time.unscaledTime of the last report of ANY kind (see RowLivesSeconds)
        public int Sig;           // order-independent hash of Cards — the allocation-free fast path
    }

    /// <summary>Keyed by "characterId/belief" so the two beliefs about one character are separate
    /// rows and a reader sees them side by side on one line.</summary>
    private static readonly Dictionary<(string Character, Belief Belief), Row> s_rows = new(8);

    /// <summary>Every character this client has drawn an active set for, in first-seen order — so
    /// the line's character order is stable across re-states and two logs stay diffable.</summary>
    private static readonly List<string> s_characters = new(8);

    /// <summary>Scratch for <see cref="Format"/>; never escapes, never resized in steady state.
    /// </summary>
    private static readonly List<CBaseCard> s_sort = new(8);
    private static readonly StringBuilder s_sb = new(256);

    /// <summary>
    /// Seconds between UNCHANGED re-states. The line is CHANGE-TRIGGERED first (the requirement is
    /// "unmittelbar", so an edge must print on the frame it happens); this cadence exists only so a
    /// client that is steadily WRONG still prints — a change-only instrument on a uniformly wrong
    /// surface says nothing at all, which is the exact failure PeerCardFaceCensus was built to
    /// escape. The number is written into the line so no reader has to come here for it.
    /// </summary>
    internal const float RestateSeconds = 15f;

    private static float s_nextRestateAt;
    private static bool s_dirty;

    /// <summary>
    /// One surface reports the active set it is DRAWING for one character. Cheap and idempotent, and
    /// safe to call every rebuild: the peer mirror's caller runs on the 4 Hz remote-content cadence,
    /// so an unchanged report must cost no allocation at all. An integer signature of the drawn set
    /// is compared first and the string is only built when that signature actually moved.
    /// </summary>
    internal static void Report(CPlayerActor? actor, Belief belief, IReadOnlyList<CAbilityCard>? drawn)
    {
        if (actor == null)
            return;
        CCharacterClass? klass = SafeClass(actor);
        if (klass == null)
            return;
        string charId = CharacterId(klass);
        // THE MODEL ROW IS STAMPED FIRST AND UNCONDITIONALLY — before the unchanged fast path
        // below, because a surface that has settled still has to keep the truth beside it alive or
        // the truth expires while the picture does not.
        StoreModel(klass, charId);
        int sig = DrawnSignature(drawn);
        if (s_rows.TryGetValue(Key(charId, belief), out Row prev) && prev.Cards != null
            && !prev.Suppressed && prev.Sig == sig)
        {
            Touch(charId, belief);
            return;
        }
        Store(charId, belief, Format(drawn), drawn?.Count ?? 0, suppressed: false, why: null, sig: sig);
    }

    /// <summary>Order-INDEPENDENT hash of a drawn set. Order-independent because the STRING is
    /// sorted: two seats holding the same cards in a different order must take the same fast path
    /// and produce the same payload, or the census would report a disagreement that is only a list
    /// order. 0 is reserved for "nothing drawn".</summary>
    private static int DrawnSignature(IReadOnlyList<CAbilityCard>? drawn)
    {
        if (drawn == null || drawn.Count == 0)
            return 0;
        int sig = 0;
        int n = 0;
        for (int i = 0; i < drawn.Count; i++)
        {
            if (drawn[i] == null)
                continue;
            sig ^= drawn[i].ID * 0x27D4EB2D; // commutative: order cannot change the result
            n++;
        }
        return sig * 31 + n;
    }

    /// <summary>Mark a row as still being reported without touching its content or its change
    /// frame — the "nothing moved" path, which must never look like an edge.</summary>
    private static void Touch(string charId, Belief belief)
    {
        (string, Belief) key = Key(charId, belief);
        if (!s_rows.TryGetValue(key, out Row row))
            return;
        row.ReportedAt = Time.unscaledTime;
        s_rows[key] = row;
    }

    /// <summary>
    /// A surface reports that it deliberately drew NOTHING, and why. ZERO IS A READING: a column
    /// that is hidden by a gate must overwrite its row rather than leave the last non-empty one
    /// standing, or the census would answer "in agreement" for the exact case the user reported
    /// (his board switched to the peer's character and showed no active card at all).
    ///
    /// <para>IT HAS NO CALLER TODAY, ON PURPOSE, AND THAT ABSENCE IS THE CONVICTING READING FOR
    /// USER ITEM 4. Its only producer was <c>CardsDriver.UpdateActive</c>'s turn gate, and deleting
    /// that gate was the fix — an active card outlives the turn that played it, so no turn-scoped
    /// term may decide whether it is drawn. The method is KEPT rather than deleted because the
    /// census still has to be able to tell "a surface refused to draw" apart from "a surface stopped
    /// reporting": <see cref="Agrees"/> reads <c>Row.Suppressed</c> and now counts a suppressed seat
    /// as a DISAGREEMENT whenever the model beside it is non-empty, so a future gate on this column
    /// that calls this method convicts itself on the very first census line instead of hiding. A
    /// future gate that just returns early hides completely — which is what happened here — so any
    /// new early-out in an active-card surface MUST call this. Grep the shipped log for
    /// "SUPPRESSED" inside an "] [Cards] ACTIVE SET" line: zero hits is the post-fix expectation,
    /// and any hit names its own gate in the clause it prints.</para>
    /// </summary>
    internal static void ReportSuppressed(CPlayerActor? actor, Belief belief, string why)
    {
        if (actor == null)
            return;
        CCharacterClass? klass = SafeClass(actor);
        if (klass == null)
            return;
        string charId = CharacterId(klass);
        // …and especially here: "this seat drew nothing" is only half a reading. The other half is
        // whether anything was SUPPOSED to be drawn, and that is the model row.
        StoreModel(klass, charId);
        Store(charId, belief, string.Empty, 0, suppressed: true, why: why, sig: 0);
    }

    /// <summary>
    /// Refresh the <see cref="Belief.Model"/> row for one character straight from
    /// <c>CCharacterClass.ActivatedCards</c>. Allocation-free unless the set actually moved: the
    /// order-dependent <see cref="Signature(CCharacterClass?)"/> is compared first and the sorted
    /// string is only rebuilt past that. Called from every report, so the model row lives and dies
    /// with the surfaces that claim to depict it.
    /// </summary>
    private static void StoreModel(CCharacterClass klass, string charId)
    {
        int sig = Signature(klass);
        if (s_rows.TryGetValue(Key(charId, Belief.Model), out Row prev) && prev.Cards != null
            && prev.Sig == sig)
        {
            Touch(charId, Belief.Model);
            return;
        }
        List<CBaseCard>? list = ActivatedCards(klass);
        int count = 0;
        string cards = FormatModel(list, ref count);
        Store(charId, Belief.Model, cards, count, suppressed: false, why: null, sig: sig);
    }

    /// <summary>
    /// How long a row may go un-reported before it stops counting as a live claim.
    ///
    /// <para>A ROW EXPIRES INSTEAD OF BEING DELETED BY A CALLER, and that is deliberate. A surface
    /// stops reporting for four different reasons — the peer left, that board is hidden by the
    /// [Net] RemoteBoards dial, the character is exhausted, the scenario ended — and wiring a
    /// forget-call into each is four chances to miss one, after which a departed player's last
    /// picture reads forever as a live disagreement. Silence is the one signal all four share, so
    /// the census reads silence directly. Generous on purpose (three re-states): a surface driven
    /// off the 4 Hz remote-content cadence must never expire just because a frame was long.</para>
    /// </summary>
    private const float RowLivesSeconds = RestateSeconds * 3f;

    /// <summary>
    /// How long a row is kept after it expires, before it is forgotten outright — long enough that
    /// "the peer's board went away holding card X" is readable for several census lines, short
    /// enough that a scenario boundary cleans itself.
    ///
    /// <para>THIS IS WHY THERE IS NO Reset() FOR A CALLER TO FORGET. An earlier draft had one, wired
    /// into <c>CardsModule.Shutdown</c>, and <c>check-instrument-writes.py</c> was right to fail it:
    /// a teardown that reads census state makes the census LOAD-BEARING — it can no longer be gated
    /// off or retired without changing behaviour, which is the shape that once nearly latched the
    /// wall fade off forever. The instrument now cleans up inside its own print, so every read and
    /// every write of these two collections lives in the instrument and deleting the whole census
    /// deletes nothing else.</para>
    /// </summary>
    private const float RowForgetSeconds = RestateSeconds * 8f;

    private static void Store(string charId, Belief belief, string cards, int count, bool suppressed,
                              string? why, int sig)
    {
        if (!s_characters.Contains(charId))
            s_characters.Add(charId);
        (string, Belief) key = Key(charId, belief);
        s_rows.TryGetValue(key, out Row row);
        bool changed = row.Cards == null
                       || !string.Equals(row.Cards, cards, System.StringComparison.Ordinal)
                       || row.Suppressed != suppressed;
        if (changed)
        {
            row.ChangedFrame = Time.frameCount;
            row.ChangedAt = Time.unscaledTime;
            s_dirty = true;
        }
        row.Cards = cards;
        row.Count = count;
        row.Suppressed = suppressed;
        row.Why = why;
        row.Sig = sig;
        row.ReportedAt = Time.unscaledTime;
        s_rows[key] = row;
    }

    /// <summary>Row key. A VALUE TUPLE, not a concatenated string: the peer mirror reports on the
    /// 4 Hz remote-content cadence and the fast path in <see cref="Report"/> looks a row up before
    /// it decides to do nothing, so building a key must not allocate.</summary>
    private static (string, Belief) Key(string charId, Belief belief) => (charId, belief);

    /// <summary>Stable, client-independent identity for a character — the game's own
    /// <c>CCharacterClass.CharacterID</c>, the character YML's ID string (CCharacterClass.cs:232,
    /// e.g. "Brute"). It is the same token on every machine AND it is human-readable, which is the
    /// whole point: two clients' lines for the same player have to be comparable side by side by a
    /// person reading two logs, not by a tool that knows each client's transport ids.</summary>
    private static string CharacterId(CCharacterClass klass)
    {
        try { return klass.CharacterID ?? "?"; }
        catch { return "?"; }
    }

    /// <summary>
    /// The comparable payload: the drawn set as sorted <c>id:Name</c> pairs. SORTED BY CARD ID, so
    /// two clients that hold the same set in a different order still produce byte-identical strings
    /// and a reader diffing two logs sees a real disagreement and nothing else.
    /// </summary>
    private static string Format(IReadOnlyList<CAbilityCard>? drawn)
    {
        if (drawn == null || drawn.Count == 0)
            return string.Empty;
        // Insertion sort by card id, in place. NOT List.Sort(Comparison<T>): that allocates a
        // comparer wrapper on every call on this runtime, and this list is at most a handful of
        // cards on a path a peer mirror can reach several times a second.
        s_sort.Clear();
        for (int i = 0; i < drawn.Count; i++)
        {
            CAbilityCard card = drawn[i];
            if (card == null)
                continue;
            int at = s_sort.Count;
            while (at > 0 && s_sort[at - 1].ID > card.ID)
                at--;
            s_sort.Insert(at, card);
        }
        s_sb.Clear();
        for (int i = 0; i < s_sort.Count; i++)
        {
            if (i > 0)
                s_sb.Append(", ");
            s_sb.Append(s_sort[i].ID).Append(':').Append(SafeName(s_sort[i]));
        }
        s_sort.Clear();
        return s_sb.ToString();
    }

    /// <summary>
    /// The model list in the SAME sorted <c>id:Name</c> form the drawn sets take, so a model row
    /// and a board row are comparable as literal strings and a mismatch is the reading.
    ///
    /// <para>The <c>is CAbilityCard</c> filter is not a narrowing — it is exactly what BOTH drawing
    /// surfaces keep (<c>RemoteActiveCards.Refresh</c>'s own <c>is CAbilityCard</c> test, and
    /// <c>CardsGameApi.GetActivePileWidgets</c>, which can only match a card that has an
    /// <c>AbilityCardUI</c>). Comparing the raw list against them would manufacture a disagreement
    /// out of a non-ability activated card that no seat was ever asked to draw.</para>
    /// </summary>
    private static string FormatModel(List<CBaseCard>? cards, ref int count)
    {
        count = 0;
        if (cards == null || cards.Count == 0)
            return string.Empty;
        s_sort.Clear();
        for (int i = 0; i < cards.Count; i++)
        {
            if (!(cards[i] is CAbilityCard card))
                continue;
            int at = s_sort.Count;
            while (at > 0 && s_sort[at - 1].ID > card.ID)
                at--;
            s_sort.Insert(at, card);
        }
        count = s_sort.Count;
        s_sb.Clear();
        for (int i = 0; i < s_sort.Count; i++)
        {
            if (i > 0)
                s_sb.Append(", ");
            s_sb.Append(s_sort[i].ID).Append(':').Append(SafeName(s_sort[i]));
            // ── WHERE THIS CARD IS GOING, AND THEREFORE WHAT IT MUST LOOK LIKE (item 4, 2026-09-07)
            // ON THE MODEL ROW ONLY, AND THAT IS THE WHOLE POINT. The other two rows are PICTURES —
            // what a seat DREW — and a destination is not something a picture can carry; putting it
            // there would be the same category error this file's own header records ("a row that is
            // not a picture of anything is the one row that cannot be fooled by every picture being
            // wrong the same way"). The model row is the only row entitled to state a fact.
            //
            // THE BLIND SPOT IT CLOSES, measured. The user's correction — "dieser Effekt war bei
            // manchen Aktiven Karten vorhanden und wurde dort auch angezeigt - aber nur eine Runde
            // - die runde darauf war die Karte wieder blau" — turns on WHICH activated cards are
            // bound for Lost, and this line carried no such term, so no line in either 71 MB
            // ModBuild 476 log could classify the two activations those logs DO record
            // ('TheMindsWeakness' at Player.log:83670, 'GnawingHorde' at :123923). A rule was
            // shipped inverted because the instrument could not answer the question it was about.
            //
            // ONE EXPRESSION, NOT A SECOND COPY: Cards.BurnLookPolicy.Destination is the game's own
            // CCharacterClass.cs:479 test and it is the very method the owner's board and the
            // mirror both enforce through.
            s_sb.Append(BurnLookPolicy.Destination(s_sort[i] as CAbilityCard) == CBaseCard.ECardPile.Lost
                        ? "(->Lost)" : "(->Discard)");
        }
        s_sort.Clear();
        return s_sb.ToString();
    }

    private static string SafeName(CBaseCard card)
    {
        try { return card.Name ?? "?"; }
        catch { return "?"; }
    }

    /// <summary>
    /// Print the census when something CHANGED (immediately, on that frame) or when the re-state
    /// cadence is due. Called once per frame from <c>CardsDriver</c>'s tick, which runs on every
    /// client in every session — single-player included, where the line degenerates to the local
    /// player's own row and is still the right answer to "what does this client think is active".
    /// </summary>
    internal static void PrintIfDue()
    {
        float now = Time.unscaledTime;
        bool due = now >= s_nextRestateAt;
        if (!s_dirty && !due)
            return;
        s_dirty = false;
        s_nextRestateAt = now + RestateSeconds;
        if (s_rows.Count == 0)
            return;

        var sb = new StringBuilder(384);
        int disagreements = 0;
        // FORGET, INSIDE THE INSTRUMENT (see RowForgetSeconds): a row nobody has reported for this
        // long belongs to a scenario that is over or to a player who has gone. Walking backwards so
        // a removal cannot skip the next entry; the print pass below then keeps first-seen order,
        // which is what makes two logs' lines diffable.
        for (int i = s_characters.Count - 1; i >= 0; i--)
        {
            string gone = s_characters[i];
            if (Expired(gone, Belief.Model, now) & Expired(gone, Belief.OwnBoard, now)
                & Expired(gone, Belief.RemoteBoard, now))
                s_characters.RemoveAt(i);
        }

        for (int i = 0; i < s_characters.Count; i++)
        {
            string charId = s_characters[i];
            bool hasModel = s_rows.TryGetValue(Key(charId, Belief.Model), out Row model);
            bool hasOwn = s_rows.TryGetValue(Key(charId, Belief.OwnBoard), out Row own);
            bool hasRemote = s_rows.TryGetValue(Key(charId, Belief.RemoteBoard), out Row remote);
            if (!hasOwn && !hasRemote)
                continue;
            bool modelLive = hasModel && now - model.ReportedAt <= RowLivesSeconds;
            bool ownLive = hasOwn && now - own.ReportedAt <= RowLivesSeconds;
            bool remoteLive = hasRemote && now - remote.ReportedAt <= RowLivesSeconds;
            // EVERY LIVE SEAT IS MEASURED AGAINST THE MODEL, NOT AGAINST THE OTHER SEAT. The old
            // rule compared the two boards to each other and excused a SUPPRESSED one as "drew
            // nothing on purpose" — which is exactly the shape of user item 4, where one board drew
            // nothing on purpose while a card was active and the other drew it correctly. Excusing
            // it made the instrument agree with the defect: 0 DISAGREE on both ModBuild 470 logs
            // across a session in which the user watched the card vanish ten times. A seat that
            // draws nothing while the model holds a card is now the LOUDEST reading here, because
            // it is the one the user reported. An EXPIRED seat is still excused — it stopped
            // reporting at all (peer gone / board hidden / character exhausted), which is silence
            // rather than a wrong picture.
            if (modelLive)
            {
                if (ownLive && !Agrees(own, model))
                    disagreements++;
                if (remoteLive && !Agrees(remote, model))
                    disagreements++;
            }
            if (sb.Length > 0)
                sb.Append(" | ");
            sb.Append('\'').Append(charId).Append("': ");
            Append(sb, "MODEL", hasModel, modelLive, model);
            sb.Append(" / ");
            Append(sb, "own board", hasOwn, ownLive, own);
            sb.Append(" / ");
            Append(sb, "remote board", hasRemote, remoteLive, remote);
        }
        if (sb.Length == 0)
            return;

        // HW-VERIFY: this line decides user item 8b (2026-09-06, "Jederzeit muss synchron bleiben
        // welche Karte aktiv ist … und das unmittelbar") and user item 4 (2026-09-07, "Sie wurde bei
        // mir NICHT sofort lokal angezeigt … Allerdings sieht mein Mitspieler sie bei mir").
        // Grep token: "] [Cards] ACTIVE SET".
        //
        // HOW TO READ IT — THE MODEL SEAT FIRST, THEN THE TWO PICTURES. Every character prints three
        // seats: MODEL is CCharacterClass.ActivatedCards itself, and the other two are what this
        // client's own control board and its mirror of that player's board are DRAWING. All three
        // are sorted by card id, so equal sets are byte-identical strings and any difference is the
        // reading. Compare each picture to the MODEL beside it, not to the other picture — two
        // boards can agree with each other and both be wrong, which is the shape user item 4 took.
        // The frame beside each set is the frame that seat's belief last CHANGED, never the frame it
        // was reported: "same set, frames 60 apart" is latency, "different sets, both many seconds
        // old" is a stuck surface, and a large "(Ns ago)" on a row whose board is not currently drawn
        // is a STALE row rather than a live claim. SUPPRESSED means a seat drew nothing on purpose
        // and names the gate that did it — and it now COUNTS as a disagreement whenever the MODEL
        // beside it is non-empty, because that is exactly the user's symptom.
        //
        // NOTHING HERE IS A WIRE READING AND NOTHING SHOULD BE. Both pictures resolve the same
        // in-memory list on this machine: the own board through CardsGameApi.GetActivePileWidgets →
        // ActiveCardSet.ActivatedCards, the mirror through ActiveCardSet.ActivatedCards directly. The
        // rules model of every actor is simulated on every client, so a "remote board" is a SECOND
        // VIEW OF THE SAME LOCAL MODEL, not a mirror of replicated data — which is why a peer could
        // draw this player's active card correctly while the player's own board drew nothing, with
        // no replication lag anywhere to blame. The only genuinely SENT length of a sender's active
        // list is record 36's, printed by "ACTIVE MATRIX HELD SEAT" as recordLen; cross-check that
        // against this MODEL row's count when a true wire question comes up.
        //
        // WHAT CONVICTS THE ITEM-4 FIX, IN NUMBERS. ModBuild 470, both logs, one card active all
        // session (151:ABILITY_CARD_TheMindsWeakness): the host logged "Active cards: 1 shown" TEN
        // times — ten re-appearances of one card — while the peer's mirror changed once, at its
        // frame 53308, and held. WORKING: for every character, 'own board' equals 'MODEL' on every
        // line, and DISAGREE reads 0 with no SUPPRESSED clause naming a turn gate anywhere in the
        // log. INERT: any line where MODEL is non-empty and 'own board' reads [none] or SUPPRESSED
        // — one such line is the bug, unfixed. STILL BEYOND THIS INSTRUMENT: a set that is drawn
        // correctly but drawn in the wrong PLACE, at the wrong size, or behind something — this line
        // reports membership only, so a correct set here plus a user still reporting an invisible
        // card moves the search to ActivePileViewer's layout and visibility, not to the set.
        VRLog.Note("Cards", $"ACTIVE SET (change-triggered, re-stated every {RestateSeconds:F0} s "
            + $"even when unchanged; frame {Time.frameCount}): {sb}. Each seat's list is sorted by "
            + "card id, so a seat and the MODEL beside it are comparable literally — a picture that "
            + "differs from the MODEL is the defect, and the frame numbers say how long it has been "
            + "wrong. Both pictures read one source (CCharacterClass.ActivatedCards via "
            + "ActiveCardSet) on THIS machine, so a difference is never replication lag and never a "
            + "stale widget cache: it is a gate or a draw. "
            + (disagreements > 0
                ? $"{disagreements} seat(s) DISAGREE with the model RIGHT NOW — a seat reading "
                  + "SUPPRESSED against a non-empty MODEL is a surface refusing to draw a card that "
                  + "IS active, which is user item 4 and not a legitimate suppression."
                : "Every seat this client draws agrees with the model."));
    }

    /// <summary>
    /// Does one drawing seat's picture match the model? A SUPPRESSED seat agrees only with an EMPTY
    /// model — "I deliberately drew nothing" is a correct picture of "nothing is active" and a wrong
    /// one of anything else. That single clause is the whole difference between this census and the
    /// one that read 0 DISAGREE through the session the user reported.
    /// </summary>
    private static bool Agrees(in Row seat, in Row model)
    {
        if (seat.Suppressed)
            return model.Count == 0;
        return string.Equals(seat.Cards, model.Cards, System.StringComparison.Ordinal);
    }

    /// <summary>Drop one seat's row if nobody has reported it for <see cref="RowForgetSeconds"/>.
    /// True when the row is gone — either just now or because it never existed.</summary>
    private static bool Expired(string charId, Belief belief, float now)
    {
        (string, Belief) key = Key(charId, belief);
        if (!s_rows.TryGetValue(key, out Row row))
            return true;
        if (now - row.ReportedAt <= RowForgetSeconds)
            return false;
        s_rows.Remove(key);
        return true;
    }

    private static void Append(StringBuilder sb, string label, bool has, bool live, in Row row)
    {
        sb.Append(label).Append(' ');
        if (!has)
        {
            sb.Append("(not drawn on this client)");
            return;
        }
        if (!live)
        {
            // EXPIRED, not wrong: this seat has stopped reporting entirely. It is printed with its
            // last picture rather than dropped, because "the peer's board went away holding card X"
            // is a reading a hidden row would hide.
            sb.Append("EXPIRED (stopped reporting; last picture [")
              .Append(row.Suppressed ? "suppressed" : row.Count == 0 ? "none" : row.Cards).Append("])");
        }
        else if (row.Suppressed)
        {
            sb.Append("SUPPRESSED [").Append(row.Why ?? "no reason given").Append(']');
        }
        else if (row.Count == 0)
        {
            sb.Append("[none]");
        }
        else
        {
            sb.Append('[').Append(row.Cards).Append(']');
        }
        sb.Append(" @frame ").Append(row.ChangedFrame)
          .Append(" (").Append((Time.unscaledTime - row.ChangedAt).ToString("F1")).Append("s ago)");
    }
}
