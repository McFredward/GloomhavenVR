using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// ONE LINE THAT ANSWERS "IS A PEER'S CARD SHOWING A FRONT OR A BACK, AND WHICH RULE DECIDED IT" —
/// for every population at once, on a stated cadence.
///
/// <para>WHY IT EXISTS, AND WHY THE PER-SURFACE LINES COULD NOT DO IT. Every remote card surface
/// already carries a diagnostic of its own ("Remote hand fan faces", "Remote pile browse fan faces",
/// "Remote item fan faces", "Remote held card FRONT"), and every one of them is CHANGE-TRIGGERED:
/// it prints on a backs↔fronts transition and says nothing in between. That is the right cadence for
/// an edge and the wrong one for the question the user is actually asking, which is about a STANDING
/// picture ("das hat wieder so gut wie garnicht funktioniert"). Two 100 MB logs of a two-hour
/// two-player session held 31 and 19 of those lines between them — far too few to describe a defect
/// that was happening constantly, and a reader who counts them measures the instrument's latch
/// rather than the game. Worse, a surface that is UNIFORMLY WRONG never crosses a transition at all
/// and therefore prints NOTHING: the held card in a scenario produced exactly zero lines in either
/// log, and that silence was the loudest fact in the evidence once it was read as a population size
/// instead of as an event count.</para>
///
/// <para>SO THIS ONE IS A SAMPLER, NOT AN EDGE. It states its own cadence inside the line
/// (<see cref="CensusSeconds"/>), it reports the LIVE picture at the tick it prints — every
/// population that drew any peer card at all, with its front/back split and the rule that decided
/// it — and it carries the interval's WORST reading beside the current one, so a fan that flickered
/// to backs for half a second while a card was plucked out of it is visible in a log nobody was
/// watching live. An absent population is itself a reading: "held card" missing from the line means
/// no peer held a card this interval, and "held card 0 FRONT / 1 BACK" means one did and lost.</para>
///
/// <para>NOTHING HERE IS A DECISION. Surfaces report what they already computed; this class stores
/// integers and formats a string. It writes no game state and allocates only its own small
/// dictionary.</para>
///
/// <para>AND A SURFACE THAT NEVER REPORTS NO LONGER SIMPLY NEVER APPEARS — that sentence stood here
/// as a reassurance and it described the defect. Every member of <see cref="Surface"/> is named on
/// every line now: one that has reported and gone quiet prints its last picture marked STALE, and
/// one that has NEVER reported since this client started is listed under NEVER ASKED with what that
/// means. The difference is the whole point. A missing row and a healthy row are indistinguishable
/// to a reader, and three rounds running the finding has been a surface nobody had on their list —
/// most recently the recess pick seat, whose 2026-09-06 rows say a wire record has never once worked
/// and which only said so because it happened to report at all.</para>
/// </summary>
internal static class PeerCardFaceCensus
{
    /// <summary>Seconds between census lines. Long enough that a busy scenario costs one line per
    /// ten seconds, short enough that a round of hardware testing produces a readable series. The
    /// number is written INTO the line, so a reader never has to come here to learn the cadence.
    /// </summary>
    internal const float CensusSeconds = 10f;

    /// <summary>The populations a peer's cards can be drawn in. Every site that decides FRONT or
    /// BACK for somebody else's card reports as exactly one of these — the enumeration IS the list
    /// of such sites, and a member that never reports is printed under NEVER ASKED rather than being
    /// absent, so "nobody has proved anything about this surface" is a sentence in the log instead
    /// of a gap a reader has to notice.
    ///
    /// <para>ADDING A MEMBER IS THEREFORE A CLAIM YOU OWE A CALL SITE FOR: a member with no
    /// <see cref="Report"/> caller will name itself on every census tick until one exists. That is
    /// deliberate and it is the direction this instrument is required to fail in.</para></summary>
    internal enum Surface
    {
        /// <summary><c>RemoteHandFan</c> — the arc floating off a peer's non-dominant hand.</summary>
        HandFan,

        /// <summary><c>RemoteHeldCardFace</c> — the card physically pinched in a peer's fist.</summary>
        HeldCard,

        /// <summary><c>RemoteControlBoard</c>'s two round-card recesses.</summary>
        RoundSlots,

        /// <summary><c>RemoteActiveCards</c> — the small active/persistent matrix beside the board.
        /// The one population with the selection-phase carve-out lifted
        /// (<c>RevealGate.PeerCardPopulation.AlreadyPublic</c>).</summary>
        ActiveMatrix,

        /// <summary><c>RemotePileFronts</c> driving <c>RemoteBrowserFan</c> — a peer's discard or
        /// burnt pile fanned out for reading.</summary>
        PileBrowse,

        /// <summary><c>RemotePileFronts</c> driving <c>RemoteItemFan</c> — a peer's equipped items.
        /// </summary>
        ItemFan,

        /// <summary><c>RemoteCardFx</c> — a card in FLIGHT into one of that peer's stacks (2026-09-06
        /// report item 5). EVENT-DRIVEN, not sampled: it reports once per flight, so its row's tick
        /// count is a count of FLIGHTS in the interval and its live front/back split is the LAST
        /// flight, not a standing picture. The interval PEAK is the reading that matters here.
        /// </summary>
        FlightSlab,

        /// <summary><c>RemoteControlBoard</c>'s round recesses drawing a card that
        /// <c>CCharacterClass.RoundAbilityCards</c> cannot name — a short-rest SACRIFICE or a modal
        /// pick's card LAID ON THE BOARD (2026-09-06 report item 7). Reported separately from
        /// <see cref="RoundSlots"/> because it is a different question with a different answer: the
        /// round slots ask the phase, this population is carved out of the phase and asks whether
        /// extension record 39 named a seat this client could resolve.
        ///
        /// <para>KEEP THIS LAST — <see cref="ReportPeerGone"/> walks the enum by ordinal.</para>
        /// </summary>
        BoardPickSeat,
    }

    private struct Entry
    {
        public int Fronts;
        public int Backs;
        public string Rule;
        public int PeakBacks;
        public string PeakRule;
        public int Samples;
    }

    /// <summary>Keyed by (surface, player id, SLOT) so two peers never overwrite each other's
    /// reading — a fan that works for one player and not for the other is precisely the shape this
    /// has to be able to show. Small and bounded: populations x peers x slots.
    ///
    /// <para>THE SLOT WAS ADDED 2026-09-06 AND IT IS A BUG FIX, NOT A REFINEMENT. A peer has TWO
    /// hands, and <c>RemoteHeldCardFace.Report</c> is called once per frame PER SLOT. Both calls
    /// landed on one key, so the second one — slot 2, which is empty almost all the time — silently
    /// overwrote the first. 175 of the 195 census lines in the ModBuild 459 host log therefore read
    /// "held card[p2] 0 FRONT / 0 BACK — slot 2: nothing in this hand" and said NOTHING WHATEVER
    /// about the hand that was actually holding a card. Only the PeakBacks term survived the clobber,
    /// which is why the one thing the log did show about slot 1 was a worst-case count with no live
    /// reading beside it. An instrument that always reports the empty half of a two-sided population
    /// is worse than no instrument, because its zeros read as health.</para></summary>
    private static readonly Dictionary<long, Entry> s_entries = new(16);

    private static float s_nextPrintAt = -1f;

    /// <summary>
    /// One bit per <see cref="Surface"/> that has reported AT LEAST ONCE since this client started —
    /// the term that turns a missing row into a stated reading.
    ///
    /// <para>WHY IT IS A DELIVERABLE AND NOT A FLOURISH. This class's own header already says that
    /// "a surface that never reports simply never appears", and treated that as harmless. It is not:
    /// a row that is absent and a row that reads 0 BACK look identical to a reader, and this is the
    /// third round in a row in which the finding was a surface nobody had on their list. The
    /// 2026-09-06 session is the proof — for the WHOLE session, on BOTH machines, every 'board pick
    /// seat' row read "extension record 39 named NO seat", which is the strongest possible statement
    /// that a wire record has never once worked, and it was reachable only because that surface
    /// happened to report. Had it been one branch further up it would have printed nothing at all
    /// and the silence would have read as health. So the line now names EVERY member of the enum on
    /// every tick, and a member with no bit here is printed as NEVER ASKED with what that means.
    /// </para></summary>
    private static int s_everReported;

    /// <summary>Slot ids are 1 and 2 (a peer's two hands); 0 means "this population has only one
    /// reporter" and is what every surface but the held card passes.</summary>
    private static long Key(Surface surface, int playerId, int slot) =>
        ((long)playerId << 16) | ((long)(byte)slot << 8) | (byte)surface;

    /// <summary>
    /// Report this frame's verdict for one peer's one population. <paramref name="rule"/> is the
    /// SHORT name of what decided it (the reveal-gate answer, "length belt", "no source", …) and is
    /// quoted verbatim into the line — it is the half of the census that says WHY, and a caller that
    /// passes a vague one makes the next hardware round unreadable.
    /// </summary>
    internal static void Report(Surface surface, int playerId, int fronts, int backs, string rule,
                                int slot = 0)
    {
        long key = Key(surface, playerId, slot);
        s_entries.TryGetValue(key, out Entry e);
        e.Fronts = fronts;
        e.Backs = backs;
        e.Rule = rule;
        e.Samples++;
        s_everReported |= 1 << (int)surface;
        // The interval's WORST reading, kept beside the live one: a surface that is correct at the
        // moment the cadence happens to fire, and wrong for the second the player was looking at it,
        // must not read as healthy. Ties keep the FIRST rule seen, so a peak carries the reason it
        // first appeared for rather than the last one to match it.
        if (backs > e.PeakBacks)
        {
            e.PeakBacks = backs;
            e.PeakRule = rule;
        }
        s_entries[key] = e;
    }

    /// <summary>Forget everything a peer reported (they left, or their avatar was torn down), so a
    /// stale row cannot outlive the player it describes.</summary>
    internal static void ReportPeerGone(int playerId)
    {
        // EVERY SURFACE AND EVERY SLOT. Two independent fixes of 2026-09-06 meet here and both
        // bounds are load-bearing: the surface bound must reach the LAST enum member (the enum's
        // own doc keeps BoardPickSeat there for exactly this walk), and the slot bound exists
        // because the held-card population reports under slots 1 and 2 — a single-slot sweep left
        // rows that outlived the player they describe, which is the stale reading this method
        // exists to prevent. Both bounds are stated here rather than discovered by a reader.
        for (int s = 0; s <= (int)Surface.BoardPickSeat; s++)
        {
            for (int slot = 0; slot <= 2; slot++)
                s_entries.Remove(Key((Surface)s, playerId, slot));
        }
    }

    /// <summary>
    /// THE PHASE AND THE FACE POLICY IT SELECTS, in one clause the next round can grep instead of
    /// asking again.
    ///
    /// <para>WHY IT IS HERE AND NOT LEFT TO A READER. The user asked, in as many words, whether it
    /// can be GUARANTEED that he never sees a back outside the selection phase — left hand, right
    /// hand, handing over, opening or closing the fan while holding, on the map. Every log this
    /// project has produced answers that question with silence: the census counted BACKs beautifully
    /// and named NEITHER the phase they happened in nor the rule the phase selects, so "a BACK
    /// outside the selection window is a defect" was a sentence no reader could evaluate. Two
    /// rounds were spent on item 7 partly for that reason — the reveal gate was open the whole time
    /// and nothing in either 62 MB log said so in a form a grep could reach.</para>
    ///
    /// <para>IT READS THE GAME'S OWN AUTHORITY and nothing of the mod's:
    /// <c>PhaseManager.PhaseType</c> is a static read of <c>s_CurrentPhase.Type</c> that answers
    /// <c>PhaseType.None</c> with no phase object, so it cannot throw and cannot be starved by a
    /// half-loaded save (see <see cref="RevealGate.IsSecretSelectionPhase"/>'s own note). The
    /// try/catch is for <c>FFSNetwork.IsOnline</c>'s Bolt read, not for the phase.</para>
    /// </summary>
    private static string PhaseAndPolicy()
    {
        try
        {
            ScenarioRuleLibrary.CPhase.PhaseType phase = ScenarioRuleLibrary.PhaseManager.PhaseType;
            bool secret = RevealGate.IsSecretSelectionPhase;
            bool online = FFSNetwork.IsOnline;
            return $"PHASE={phase}, online={online}, POLICY="
                 + (secret && online
                     ? "BACKS ARE LAWFUL for a remote character's hand, held card, round slots, "
                       + "pile arcs and item fan — this IS the game's own "
                       + "SelectAbilityCardsOrLongRest window and the two-card commit it protects "
                       + "is in flight. The active matrix and a short-rest sacrifice are carved out "
                       + "of even this and must still read 0 BACK"
                     : "FRONTS EVERYWHERE — no secret is in flight, so ANY non-zero BACK below is a "
                       + "defect and the rule beside it names which one. A long rest is an ACTION "
                       + "and lands here, not in the window above, whichever phase it resolves in");
        }
        catch (System.Exception ex)
        {
            // A phase we cannot read is not a phase we may claim. Say so rather than printing a
            // policy nobody measured.
            return $"PHASE=unreadable ({ex.GetType().Name}), POLICY=unknown — read no guarantee "
                 + "out of the counts below";
        }
    }

    /// <summary>
    /// Drive the cadence — called once per frame from the avatar tick, after every surface has had
    /// its say. Prints nothing while no peer card has been drawn at all.
    /// </summary>
    internal static void PrintIfDue()
    {
        float now = Time.unscaledTime;
        if (s_nextPrintAt < 0f)
        {
            s_nextPrintAt = now + CensusSeconds;
            return;
        }
        if (now < s_nextPrintAt)
            return;
        s_nextPrintAt = now + CensusSeconds;
        if (s_entries.Count == 0)
            return;

        var sb = new System.Text.StringBuilder(512);
        int totalFronts = 0;
        int totalBacks = 0;
        int worstBacks = 0;
        int staleRows = 0;
        foreach (KeyValuePair<long, Entry> kv in s_entries)
        {
            var surface = (Surface)(byte)(kv.Key & 0xFF);
            int slot = (int)((kv.Key >> 8) & 0xFF);
            int playerId = (int)(kv.Key >> 16);
            Entry e = kv.Value;
            // ─── A ROW NOBODY REPORTED THIS INTERVAL IS NOT A PICTURE OF THIS INTERVAL ──────────
            // The live values are deliberately KEPT when a surface stops reporting, so a population
            // that goes quiet shows its last picture instead of silently reading zero. That is the
            // right thing on screen and it was WRONG in the arithmetic: those kept numbers were
            // being added into the totals, so the guarantee counter this file documents
            // ("grep POLICY=FRONTS | grep -vc 'and 0 showing a BACK right now'") convicted the mod
            // on rows that were not being drawn at all. It read 75 breaches out of 165 policy-FRONTS
            // ticks on the host of the 2026-09-06 session and 5 out of 166 on the co-player, and on
            // the host EVERY ONE of the 75 was one row — "pile browse[p2] 0 FRONT / 1 BACK ... over
            // 0 tick(s)" — a closed browse fan whose last reading was a legitimate selection-phase
            // back from minutes earlier. A stale row is now labelled and left OUT of the totals; it
            // still prints, because "this surface stopped reporting while showing a back" is itself
            // a reading, and hiding it would be the opposite mistake.
            bool stale = e.Samples == 0;
            if (stale)
                staleRows++;
            else
            {
                totalFronts += e.Fronts;
                totalBacks += e.Backs;
                if (e.PeakBacks > worstBacks)
                    worstBacks = e.PeakBacks;
            }
            if (sb.Length > 0)
                sb.Append("; ");
            sb.Append(Name(surface)).Append("[p").Append(playerId);
            if (slot > 0)
                sb.Append("/slot").Append(slot);
            sb.Append("] ")
              .Append(e.Fronts).Append(" FRONT / ").Append(e.Backs).Append(" BACK — ")
              .Append(e.Rule ?? "(no rule reported)");
            if (e.PeakBacks > e.Backs)
                sb.Append(" (worst this interval: ").Append(e.PeakBacks).Append(" BACK — ")
                  .Append(e.PeakRule ?? "(no rule reported)").Append(')');
            sb.Append(stale
                ? " over 0 tick(s) — STALE, this surface drew NOTHING this interval and the numbers "
                  + "above are its last picture; excluded from the totals"
                : " over " + e.Samples + " tick(s)");
        }

        // ─── AND THE SURFACES THAT PRODUCED NO ROW AT ALL ───────────────────────────────────────
        // An absent population used to be silence, and silence is the one reading a hardware log
        // cannot distinguish from health. Every member of the enum is named on every tick now: one
        // that has never reported since this client started says so in those words, so "we have
        // measured nothing about this surface" is a sentence a grep can find instead of an absence
        // a reader has to notice. See s_everReported for the round this cost.
        int neverAsked = 0;
        for (int surfaceId = 0; surfaceId <= (int)Surface.BoardPickSeat; surfaceId++)
        {
            if ((s_everReported & (1 << surfaceId)) != 0)
                continue;
            if (neverAsked++ == 0)
                sb.Append("; NEVER ASKED (no site has reported these since this client started, so "
                          + "NOTHING has been measured about them and their absence is not a pass): ");
            else
                sb.Append(", ");
            sb.Append(Name((Surface)surfaceId));
        }

        // HW-VERIFY: THE line for the 2026-09-05 report item 2 ("Das Anzeigen der remote Karten
        // funktioniert immer noch nicht") and, since ModBuild 459, THE answer to the user's standing
        // guarantee question ("Gewaehrleiste das ausserhalb der Auswahlphase NIEMALS Rueckseiten auf
        // Vorderseiten angezeigt werden ... IMMER OHNE AUSNAHME"). Grep token:
        // PEER CARD FACE CENSUS.
        //
        // HOW TO ANSWER THE GUARANTEE FROM A LOG, with no arithmetic and no screenshot. The
        // DENOMINATOR — every sampled tick in which no secret was in flight, so every tick the
        // guarantee is a claim about:
        //     grep -a '\] \[Net\] PEER CARD FACE CENSUS' Player.log | grep -c 'POLICY=FRONTS'
        // and the NUMERATOR — the ticks in which it was BROKEN:
        //     grep -a '\] \[Net\] PEER CARD FACE CENSUS' Player.log \
        //       | grep 'POLICY=FRONTS' | grep -vc 'and 0 showing a BACK right now'
        // A zero numerator against a non-zero denominator IS the guarantee, measured rather than
        // asserted; a zero DENOMINATOR means the session never left the selection phase with a peer
        // card on screen and this log says nothing about it either way. Every line the numerator
        // counts carries the per-population rule that decided it, and that rule names which of the
        // five causes it was — a SHUT gate (which POLICY=FRONTS says is impossible, so it would
        // indict the gate itself), an OPEN gate with nothing resolved, the length belt, a seat the
        // sender could not name, or a board that is not being drawn. Those five used to be four
        // strings, two of which shared one sentence; see RemoteHandFan._censusGateShut.
        //
        // It is a SAMPLER on a
        // fixed cadence, not a change edge, so a population that is uniformly wrong prints here
        // every ten seconds instead of printing nothing at all — which is exactly how the previous
        // round's evidence went silent. READ IT LIKE THIS: outside the secret selection phase, any
        // non-zero BACK count is the defect, and the rule quoted beside it names which of the four
        // possible causes it was (the reveal gate, the length belt, an unresolved source, or a seat
        // the sender could not name). 'active matrix' showing BACKs is the defect in EVERY phase,
        // including the selection window — that population is the user's explicit carve-out. The
        // FALSIFIER for the fix in this build: a line that still shows 'hand fan' or 'held card'
        // with BACKs while no selection phase is running means the fix is inert, not that the
        // defect moved; a line in which those populations never appear at all means the census is
        // not being reached and nothing here has been measured.
        VRLog.Note("Net", $"PEER CARD FACE CENSUS (every {CensusSeconds:F0} s, the LIVE picture at "
            + $"this tick — a sample, never a count of events): {PhaseAndPolicy()}. {totalFronts} "
            + $"peer card(s) showing a FRONT and {totalBacks} showing a BACK right now"
            + (worstBacks > totalBacks ? $" (worst reading in the interval: {worstBacks} BACK)" : string.Empty)
            + (staleRows > 0 ? $" ({staleRows} STALE row(s) excluded — see below)" : string.Empty)
            + $". Per population — {sb}. The rule beside each population is the one that actually "
            + "decided it, so a BACK outside the game's own SelectAbilityCardsOrLongRest window is a "
            + "defect and the rule names which one. A row marked STALE drew nothing this interval "
            + "and counts toward NOTHING: its numbers are the last picture that surface had, kept so "
            + "a population that goes quiet does not read as zero. A surface listed under NEVER "
            + "ASKED is a different and worse reading than a STALE one: STALE means it reported "
            + "earlier and has gone quiet, NEVER ASKED means no code path has ever handed this "
            + "census a verdict for it and this log therefore proves nothing about it either way — "
            + "which is exactly how a whole surface hid for two rounds. 'active matrix' must NEVER show a "
            + "BACK in any phase (user ruling 2026-09-05: an active card was played face-up, it is "
            + "no secret). 'flight slab' is EVENT-DRIVEN — its ticks are FLIGHTS, not samples, so "
            + "read its interval PEAK and not its live split (report item 5). 'board pick seat' 0 "
            + "FRONT / n BACK is report item 7's card lying on the board: read the rule, which says "
            + "whether record 39 named a seat or whether this client could not resolve one.");

        // Peaks are per-interval; the live values stay so a population that stops reporting keeps
        // its last picture rather than silently reading zero.
        var keys = new List<long>(s_entries.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            Entry e = s_entries[keys[i]];
            e.PeakBacks = e.Backs;
            e.PeakRule = e.Rule;
            e.Samples = 0;
            s_entries[keys[i]] = e;
        }
    }

    private static string Name(Surface surface) => surface switch
    {
        Surface.HandFan => "hand fan",
        Surface.HeldCard => "held card",
        Surface.RoundSlots => "round slots",
        Surface.ActiveMatrix => "active matrix",
        Surface.PileBrowse => "pile browse",
        Surface.ItemFan => "item fan",
        Surface.FlightSlab => "flight slab",
        Surface.BoardPickSeat => "board pick seat",
        _ => surface.ToString(),
    };
}
