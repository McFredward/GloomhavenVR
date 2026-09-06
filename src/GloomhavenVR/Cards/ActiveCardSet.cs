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
/// <c>CCharacterClass.ActivatedCards</c>, the list <c>CCharacterClass.ActivateCard</c>
/// (CCharacterClass.cs:362) appends to at the instant of activation;</description></item>
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
/// the game only ever drives the LOCAL player's hand view — plus a turn gate, see
/// <c>CardsGameApi.IsPresentedActorTurn</c>). No wire field could have fixed any of it: the fact
/// was already on all three machines. It was read from the wrong place on two of them.</para>
///
/// <para>SO EVERY SURFACE ASKS HERE NOW, and the answer is the model, always. Sibling lanes that
/// need "is this card currently active" — the burn-presentation trigger (activated-but-not-yet-lost
/// must not look or sound burnt) and the face policy (an active card taken into the hand shows its
/// FRONT even in the selection phase) — call <see cref="IsActive(CPlayerActor, CBaseCard)"/> and
/// get the same answer this column draws, by construction rather than by agreement.</para>
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
    /// true on the frame <c>CCharacterClass.ActivateCard</c> ran and on every client that has the
    /// actor — not on the frame the game next refreshes a 2D hand view.
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
    /// </summary>
    internal static void ReportSuppressed(CPlayerActor? actor, Belief belief, string why)
    {
        if (actor == null)
            return;
        CCharacterClass? klass = SafeClass(actor);
        if (klass == null)
            return;
        Store(CharacterId(klass), belief, string.Empty, 0, suppressed: true, why: why, sig: 0);
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
            if (Expired(gone, Belief.OwnBoard, now) & Expired(gone, Belief.RemoteBoard, now))
                s_characters.RemoveAt(i);
        }

        for (int i = 0; i < s_characters.Count; i++)
        {
            string charId = s_characters[i];
            bool hasOwn = s_rows.TryGetValue(Key(charId, Belief.OwnBoard), out Row own);
            bool hasRemote = s_rows.TryGetValue(Key(charId, Belief.RemoteBoard), out Row remote);
            if (!hasOwn && !hasRemote)
                continue;
            bool ownLive = hasOwn && now - own.ReportedAt <= RowLivesSeconds;
            bool remoteLive = hasRemote && now - remote.ReportedAt <= RowLivesSeconds;
            // A SUPPRESSED seat is not a disagreement about CONTENT — it is a seat that drew
            // nothing on purpose, and its own clause already says which gate did it. Neither is an
            // EXPIRED seat, which stopped reporting at all (peer gone / board hidden / character
            // exhausted). Counting either would drown the real unequal-set case.
            if (ownLive && remoteLive && !own.Suppressed && !remote.Suppressed
                && !string.Equals(own.Cards, remote.Cards, System.StringComparison.Ordinal))
                disagreements++;
            if (sb.Length > 0)
                sb.Append(" | ");
            sb.Append('\'').Append(charId).Append("': ");
            Append(sb, "own board", hasOwn, ownLive, own);
            sb.Append(" / ");
            Append(sb, "remote board", hasRemote, remoteLive, remote);
        }
        if (sb.Length == 0)
            return;

        // HW-VERIFY: this line decides user item 8b ("Jederzeit muss synchron bleiben welche Karte
        // aktiv ist … und das unmittelbar"). Grep token: "] [Cards] ACTIVE SET". It is the
        // instrument the 2026-09-06 evidence did NOT have: the only active-card lines in those two
        // 85/72 MB logs were COUNTS ("Active cards: 1", "Peer [2] active grid: 1 card(s)"), and a
        // count cannot answer a question about WHICH card. This one names the cards.
        //
        // HOW TO READ IT. Print it from BOTH logs, pick one character, and compare the bracketed
        // list of every seat that names it. The sets are sorted by card id, so equal sets are
        // byte-identical strings and any difference IS the desync. The frame beside each set is the
        // frame that seat's belief last CHANGED, never the frame it was reported: "same set, frames
        // 60 apart" is a latency reading, "different sets, both many seconds old" is a stuck
        // surface, and a large "(Ns ago)" on a row whose board is not currently drawn is a STALE row
        // rather than a live claim. A seat reading SUPPRESSED drew nothing on purpose and names the
        // gate that did it.
        //
        // WHAT CONVICTS THIS FIX. On 2026-09-06 the owner's own board trailed its own mirrors by
        // 390 s and 520 s (host frame 107724 vs peer frame 74216; peer frame 37174 vs host frame
        // 142334). If a post-fix log shows any character whose 'own board' and 'remote board' sets
        // differ for more than a couple of frames outside a SUPPRESSED clause, this fix did not
        // work. If DISAGREE reads 0 for a whole session and the user still reports the symptom, the
        // defect is downstream of the set — in what is DRAWN, not in what is believed — and this
        // instrument has exonerated the sync and named where to look next.
        VRLog.Note("Cards", $"ACTIVE SET (change-triggered, re-stated every {RestateSeconds:F0} s "
            + $"even when unchanged; frame {Time.frameCount}): {sb}. Each seat's list is sorted by "
            + "card id, so two clients' lines for the SAME character are comparable literally — an "
            + "unequal pair is the 1:1 breach and the frame numbers say which seat is behind. Every "
            + "seat reads one source (CCharacterClass.ActivatedCards via ActiveCardSet), so an "
            + "unequal pair can no longer be a stale widget cache and means real replication lag. "
            + (disagreements > 0
                ? $"{disagreements} character(s) DISAGREE between this client's own board and its "
                  + "mirror of that player's board RIGHT NOW."
                : "This client's own board and its remote mirrors agree on every character it "
                  + "draws."));
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
