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
/// integers and formats a string. It writes no game state, allocates only its own small dictionary,
/// and a surface that never reports simply never appears.</para>
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
    /// of such sites, so a surface missing from a hardware log is a surface that never reported and
    /// therefore one nobody has proved anything about.</summary>
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

    /// <summary>Keyed by (surface, player id) so two peers never overwrite each other's reading —
    /// a fan that works for one player and not for the other is precisely the shape this has to be
    /// able to show. Small and bounded: populations x peers.</summary>
    private static readonly Dictionary<long, Entry> s_entries = new(16);

    private static float s_nextPrintAt = -1f;

    private static long Key(Surface surface, int playerId) => ((long)playerId << 8) | (byte)surface;

    /// <summary>
    /// Report this frame's verdict for one peer's one population. <paramref name="rule"/> is the
    /// SHORT name of what decided it (the reveal-gate answer, "length belt", "no source", …) and is
    /// quoted verbatim into the line — it is the half of the census that says WHY, and a caller that
    /// passes a vague one makes the next hardware round unreadable.
    /// </summary>
    internal static void Report(Surface surface, int playerId, int fronts, int backs, string rule)
    {
        long key = Key(surface, playerId);
        s_entries.TryGetValue(key, out Entry e);
        e.Fronts = fronts;
        e.Backs = backs;
        e.Rule = rule;
        e.Samples++;
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
        for (int s = 0; s <= (int)Surface.ItemFan; s++)
            s_entries.Remove(Key((Surface)s, playerId));
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
        foreach (KeyValuePair<long, Entry> kv in s_entries)
        {
            var surface = (Surface)(byte)(kv.Key & 0xFF);
            int playerId = (int)(kv.Key >> 8);
            Entry e = kv.Value;
            totalFronts += e.Fronts;
            totalBacks += e.Backs;
            if (e.PeakBacks > worstBacks)
                worstBacks = e.PeakBacks;
            if (sb.Length > 0)
                sb.Append("; ");
            sb.Append(Name(surface)).Append("[p").Append(playerId).Append("] ")
              .Append(e.Fronts).Append(" FRONT / ").Append(e.Backs).Append(" BACK — ")
              .Append(e.Rule ?? "(no rule reported)");
            if (e.PeakBacks > e.Backs)
                sb.Append(" (worst this interval: ").Append(e.PeakBacks).Append(" BACK — ")
                  .Append(e.PeakRule ?? "(no rule reported)").Append(')');
            sb.Append(" over ").Append(e.Samples).Append(" tick(s)");
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
            + $". Per population — {sb}. The rule beside each population is the one that actually "
            + "decided it, so a BACK outside the game's own SelectAbilityCardsOrLongRest window is a "
            + "defect and the rule names which one. 'active matrix' must NEVER show a BACK in any "
            + "phase (user ruling 2026-09-05: an active card was played face-up, it is no secret).");

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
        _ => surface.ToString(),
    };
}
