using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// ONE LINE PER PILE FLIGHT, NAMING THE GAME EVENT THAT STARTED IT AND WHETHER THIS CARD HAS
/// ALREADY FLOWN THERE — the instrument for the 2026-09-07 report items 5 and 7.
///
/// <para>WHY IT EXISTS. Item 5, verbatim: "Die Animation dass die Karte in den Verbrannt-Stapel
/// geht ist direkt bei der ersten Karte gestartet … Die Animation kam noch ZUSÄTZLICH am Ende des
/// Zuges." A card flew into the burnt stack TWICE. Nothing in the shipped log could say that. The
/// two flights are produced by two different classes on two different clocks — <c>RemoteBurnFx</c>
/// off the peer's host-replicated Lost pile, <c>RemoteCardFx</c> off the owner's wire event — and
/// each of them logged its own flight, correctly, in its own words, with its own token. Proving
/// they were the SAME card's SECOND flight took reading two instruments in two files and
/// correlating them by hand across 230,000 log lines. This states it as a number on the line
/// itself.</para>
///
/// <para>WHAT THE KEY IS, AND WHY IT IS NOT THE CARD NAME. A mirrored wire flight
/// (<c>RemoteCardFx</c>) draws an ANONYMOUS slab: no card identity ever rides that wire, by
/// design, and the anti-cheat line that keeps it that way is not moving for a diagnostic. So the
/// duplicate cannot be detected by name — one of the two producers has no name to give. The key is
/// therefore <c>(whose board, which destination stack)</c>, which BOTH producers know exactly, and
/// the card name rides along as a FIELD when the producer happens to have one.</para>
///
/// <para>THE THREE READINGS, IN NUMBERS. Grep token: <c>PILE FLIGHT</c>.</para>
/// <list type="bullet">
/// <item><description><b>WORKING</b> — every <c>-&gt; Burnt</c> line in a session reads
/// <c>flight #1</c> for its key. A burned card leaves <c>CCharacterClass.LostAbilityCards</c> only
/// by a recovery effect, so within one scenario a second Burnt flight on the same board is a
/// duplicate almost by definition; <c>gap</c> then names how long after the first it came.
/// Discard repeats are LEGITIMATE (a discarded card is drawn again and discarded again) and read
/// <c>#2, #3 …</c> with a <c>gap</c> of tens of seconds — read the <c>gap</c>, never the ordinal
/// alone, on a Discard key.</description></item>
/// <item><description><b>INERT (the defect still standing)</b> — two <c>-&gt; Burnt</c> lines for
/// the same <c>[peer N]</c> whose <c>trigger</c> fields differ, with a <c>gap</c> under ~30 s.
/// That IS report item 5: on ModBuild 470 the pair was <c>remote-burn-mirror</c> (fired on the
/// 2.0 s ceiling while the owner's card was still lying in his recess) and <c>remote-wire-fx</c>
/// (fired when the owner really cleared the card at turn end). The measured evidence is the host
/// log's <c>BURN FLIGHT</c> lines at 166904/221429/229787 — three of four reading "the 2.0s
/// ceiling ran out while their recess was STILL drawing the card" — beside its <c>Remote card
/// FX … Slot1 -&gt; Burnt playing</c> at 230415 and <c>Slot0 -&gt; Burnt</c> at 230510.</description></item>
/// <item><description><b>STILL BEYOND THE INSTRUMENT</b> — the user reports a doubled flight and
/// every key reads <c>#1</c>. The second picture is then not a FLIGHT at all: the candidates are
/// the recess mirror still drawing a card the owner has cleared (<c>RemoteBoardCard</c>), a
/// burnt-pile fan re-laying out (<c>RemotePileFronts</c>), or the owner's own charred slab left
/// visible after its arc. None of those pass through here and none of them should.</description></item>
/// </list>
///
/// <para>COST. One dictionary lookup and one <c>VRLog.Note</c> per flight — a handful per turn,
/// never per frame. Nothing here reads or writes game state; <see cref="PhaseName"/> is a static
/// enum read (<c>PhaseManager.PhaseType</c>) wrapped in a catch so a game-side shape change can
/// never take a card animation down with it.</para>
///
/// <para>─────────────────────────────────────────────────────────────────────────────────────</para>
///
/// <para>THE INVENTORY OF EVERY CARD FLIGHT THIS MOD CAN START (2026-09-07, user item 1: "Mach
/// bitte eine allgemeine Bestandsaufnahme aller Flüge — wann &amp; wohin und prüfe ob es passt").
/// The user has reported a spurious flight in FOUR separate rounds and each round fixed one
/// producer. This table is the reason that stops: it is the whole population, so a fix can be
/// checked against every sibling instead of against the one that was reported.</para>
///
/// <para>THIS LEDGER COVERS 8 OF THE 28 ROWS, AND THAT WAS FINDING NUMBER ONE. The destination
/// vocabulary below was <c>Discard</c> / <c>Burnt</c> / <c>SlotN</c> — shaped on the pile stacks —
/// so the ACTIVE COLUMN, the HAND FAN and every OUT-of-a-pile flight were outside it by
/// construction. Item 1's flight was in none of the 18 <c>PILE FLIGHT</c> lines of the ModBuild 472
/// host log, which is precisely why six rounds of reading those lines never found it.</para>
///
/// <para>LINE NUMBERS: the method heads were re-grepped at ModBuild 480 (2026-09-08); the
/// <c>note :N</c> offsets are from the 2026-09-07 reading. Every one rots with the next edit —
/// grep the SYMBOL before quoting either (review R3 N5 found all of them wrong within a day).</para>
///
/// <para><b>A — the movement engines.</b> Not producers; every row below drives one of these.
/// <c>VRCard.FlyToPile</c> (VRCard.cs:1583) and <c>VRCard.FlyFromPile</c> (:1637) are explicit
/// arcs with a captured start pose. <c>VRCard.SetHome(..., instant:false)</c> (:879, ticked :2243)
/// is an exponential home GLIDE — every layout that re-seats a card animates it, which is how a
/// "layout" becomes a "flight" without anybody calling it one.</para>
///
/// <list type="table">
/// <listheader><description>#  PRODUCER — TRIGGER — DESTINATION — STATE THAT MUST HOLD — VERDICT</description></listheader>
///
/// <item><description><b>B. OWN BOARD, LEDGERED (8).</b></description></item>
/// <item><description>1. <c>CardsDriver.TryStartFlyToPile</c> (4.Rebuild:2566, note :2517) — a card
/// left the round dock / pick field AND <c>RoundCardExitOf</c> says it really left
/// <c>RoundAbilityCards</c> — Discard or Burnt stack — end of turn or a pick commit — <b>CORRECT</b>
/// (directed transition, not a count).</description></item>
/// <item><description>2. <c>CardsDriver.TryStartBurnFly</c> (4.Rebuild:2975, note :2825) — rebuild
/// park sweep / short-rest commit, gated by <c>IsFreshBurn</c> — Burnt stack — <b>CORRECT</b>.</description></item>
/// <item><description>3. <c>CardsDriver.LaunchBurnFlight</c> (4.Rebuild:3906, note :3585) — the
/// burnt-pile watcher saw a new widget, or a hand/character switch flushed a hold — Burnt stack —
/// <b>CORRECT</b>.</description></item>
/// <item><description>4. <c>CardsDriver.BurnSlab.Launch</c> (4.Rebuild:4036, note :3626) — same as
/// 3 with no live <c>VRCard</c> to fly — Burnt stack — <b>CORRECT</b> (the honest fallback).</description></item>
/// <item><description>5. <c>CardsDriver.FlyLockedPicksToPile</c> (5.Interactions:1445, note :1430)
/// — tray CONFIRM page-turn during an event discard — Discard stack — <b>CORRECT</b>.</description></item>
/// <item><description>6. <c>CardsDriver.FlyShortRestCardToDiscard</c> (5.Interactions:1841, note
/// :1793) — short-rest redraw replaces the offered sacrifice — Discard stack — <b>CORRECT</b>.</description></item>
/// <item><description>7. <c>RemoteBurnFx.Handover</c> (RemoteBurnFx.cs:1398, note :952) — the 12.5 Hz
/// walk (<c>RemoteBurnFx.WatchSeconds</c> = 0.08 s) of the peer's host-replicated Lost pile found a new widget, and their recess stopped
/// drawing it — that peer's Burnt stack — <b>CORRECT</b>; verified 7/7 against the peer's own log
/// this session, by gap AND by card name.</description></item>
/// <item><description>8. <c>RemoteCardFx.Play</c> (RemoteCardFx.cs:184, note :260) — the peer's
/// wire FX event, unless <c>RemoteBurnFx</c> swallowed it — whatever the decoded
/// <c>CardFxAnchor</c> resolves to — <b>CORRECT WHEN IT RUNS</b>; the extras stream is unreliable
/// by contract and a dropped event is a MISSING flight (see the cross-log method below).</description></item>
///
/// <item><description><b>C. OWN BOARD, NOT LEDGERED (9).</b></description></item>
/// <item><description>9. <c>CardsDriver.DrainPickReturnFlight</c> (4.Rebuild:3301) — the game's
/// "Wähle eine andere Karte" cancel — OUT of the Discard stack into the hand fan — <b>CORRECT</b>;
/// ledgered as of this build (<c>own-pick-restart-return</c>).</description></item>
/// <item><description>10. <c>ActivePileViewer.Relayout</c> (Piles/ActivePileViewer.cs:243) — ANY
/// change to the active column's contents or grid — each card's active-grid cell — <b>WAS WRONG,
/// FIXED THIS BUILD.</b> It glided EVERY column card unconditionally, so a card the game had just
/// listed in <c>ActivatedCards</c> flew out of the recess it was still lying in. That is user item
/// 1, and the restore after a damage burn (item 8) is the same producer on the way back. Arrivals
/// are now seated instantly; residents still glide when the grid re-centres. Grep
/// <c>ACTIVE SEAT</c>.</description></item>
/// <item><description>11. <c>CardsDriver.LaunchActiveFlights</c> (6.Flows:3594) — a card left a
/// round recess AND appeared in <c>ActivatedCards</c>, deduped by <c>_activeFlown</c> — OUT of the
/// recess into the active column — <b>CORRECT</b>, and it is the ONLY flight the activation story
/// should ever produce. Not ledgered because it is Lane D's file this round; it prints
/// <c>[Cards] ACTIVE FLIGHT</c>.</description></item>
/// <item><description>12. <c>CardsDriver.StartBrowseCollapse</c> (6.Flows:2997) — the pile browse
/// arc closes — that pile's stack — <b>CORRECT</b> (a browse arc must return whence it came), but
/// it draws a "card goes into a stack" picture and is INVISIBLE to this ledger. A doubled-flight
/// report whose second picture is a browse close would read as <c>#1</c> on every key.</description></item>
/// <item><description>13. <c>CardsDriver.ReturnCardToPile</c> (6.Flows:3091) — a card on loan
/// from a pile is released while its browse arc is shut — that pile's stack — <b>CORRECT</b>, same
/// blind spot as 12.</description></item>
/// <item><description>14. short-rest sacrifice present (5.Interactions:1660) — a fresh/redrawn
/// sacrifice — OUT of the Discard stack into tray Slot0 — <b>CORRECT</b>.</description></item>
/// <item><description>15. <c>CardFan.Relayout</c> / <c>BeginSwapOut</c> / <c>TickCollapse</c>
/// (CardFan.cs:1917 / :2475 / :2228) — the hand fan opens, closes or swaps character — arc slots /
/// gather point — <b>CORRECT</b> (fan motion is not a pile flight), but it is the nearest
/// look-alike to item 1 and is the first place to look if <c>ACTIVE SEAT</c> reads "in place" and
/// the user still sees a flight.</description></item>
/// <item><description>16. <c>PileBrowser</c> emerge (Piles/PileBrowser.cs:284) — a stack is poked
/// — OUT of the stack into the browse arc — <b>CORRECT</b>.</description></item>
/// <item><description>17. <c>PlayTray.PlaceCard</c> / <c>PlacePickCard</c> (Tray/PlayTray.4.Slots
/// .cs:1192 / :1221) — a card docks into a round or pick recess — the recess seat —
/// <b>CORRECT</b>.</description></item>
/// <item><description>18-19. <c>ItemChip.BeginEmerge</c> / <c>BeginCollapse</c>
/// (Piles/ItemsPile.cs:5663 / :5696) — the item fan opens/closes — the items stack —
/// <b>CORRECT</b>; item chips are not ability cards and are deliberately out of scope here.</description></item>
///
/// <item><description><b>D. MIRRORED BOARDS, NOT LEDGERED (6).</b></description></item>
/// <item><description>20-21. <c>RemoteBrowserFan.BeginEmerge</c> / <c>BeginCollapse</c>
/// (RemoteBrowserFan.cs:577 / :1037) — the peer's browse-open wire bit rises/falls — their pile
/// stack — <b>CORRECT</b>, and the mirror of rows 12/16, so the two ends stay 1:1.</description></item>
/// <item><description>22. <c>RemoteHandFan.BeginSwap</c> (RemoteHandFan.cs:630) — the peer's
/// presented character changes — gather point / arc slot — <b>CORRECT</b>, mirror of row 15.</description></item>
/// <item><description>23-25. <c>RemoteItemFan</c> emerge / collapse / solo
/// (RemoteItemFan.cs:994 / :1565 / :1382) — the peer's item fan — their items stack —
/// <b>CORRECT</b>, mirror of rows 18-19.</description></item>
///
/// <item><description><b>E. NOT PRODUCERS, checked and cleared.</b> <c>ActiveCardSet</c> (pure
/// model accessor + census, touches no transform), <c>RemoteActiveCards</c> (seats its cells with
/// <c>RemoteBoardCard.Move</c>, a direct <c>localPosition</c> write with NO interpolation — it is
/// the project's "fifth un-glided arc" backlog item and this round did not change that),
/// <c>RemoteCardPlume</c> (particles parented at identity, no travel), <c>Cards.Art.BurnCardFx</c>
/// (reparents smoke), <c>Net.NetCardFx</c> (outbox only — the RECEIVER animates, row 8).</description></item>
/// </list>
///
/// <para>THE 1:1 READING OF THE TABLE. Row 10 had no mirror counterpart at all — the owner glided
/// on activation and every peer's column (row E, <c>RemoteActiveCards</c>) snapped. So the mod was
/// breaking the standing 1:1 ruling in the direction "the owner sees a flight nobody else sees".
/// Seating the arrival makes BOTH ends still on the activation edge and leaves BOTH ends animated
/// on the end-of-turn edge (row 11 locally, row 8 with <c>to == Active</c> on every peer), which is
/// the only arrangement in which the two boards agree.</para>
///
/// <para>THE CROSS-LOG METHOD, because a MISSING flight leaves no line and no single log can see
/// one. Both clients simulate every actor, so the owner's own <c>PILE FLIGHT [own]</c> chain and
/// the observer's <c>PILE FLIGHT [peer N]</c> chain for the same board are the SAME events counted
/// twice. Each line carries a <c>gap</c> to the previous flight on ITS key, so CUMULATIVE-SUM each
/// chain and align the two sums; a flight the observer never drew shows up as a sum the owner has
/// and the observer does not. WORKED, ModBuild 472: the peer's own Discard chain summed to
/// 0 / 0 / 649.76 / 784.34 / 1136.38 s and the host's mirror of it to 0 / 784.31 / 1136.45 — so
/// TWO of that player's five discard flights never played on the observer's machine. Do not read
/// that as a routing bug without more: <c>RemoteBurnFx.ConsumesWireEvent</c> refuses any endpoint
/// whose <c>To</c> is not <c>Burnt</c> and so cannot eat a discard, the Discard and Burnt anchors
/// are <c>PileSpacing</c> apart by the same expression the LOCAL board uses
/// (<c>RemoteControlBoard.AnchorLocal</c> vs <c>PileViewer</c>, both <c>±spacing*0.5</c>, Oak
/// default 0.116 m) so they cannot collapse onto one stack, and the host's own
/// <c>CARD FX LOST</c> line already reported 1 dropped event of 4 seen on an UNRELIABLE stream.
/// A missing flight is the well-understood cost of that stream; a MISROUTED one would be new.</para>
///
/// <para>WHICH IS WHAT <c>resolvedTo</c> IS FOR (2026-09-07 item 4c: "Die Fluganimation beider
/// Karten ging aber in den Verbrannt Stapel beim remote board. Lokal war es richtig bei ihm."). The
/// <c>destination</c> field is the name the PRODUCER INTENDED, never the world point the slab
/// actually flew to, so a log full of correct labels cannot falsify "it went to the wrong stack" —
/// the instrument and the claim never meet. <c>resolvedTo</c> carries the endpoint the flight was
/// really aimed at, named by the stack it is NEAREST to. A line whose <c>destination</c> and
/// <c>resolvedTo</c> DISAGREE is item 4c, named, in one grep; a session in which they agree on
/// every line moves the question to the cross-log method above, where item 4c currently sits.</para>
/// </summary>
internal static class CardFlightLedger
{
    /// <summary>Per <c>(board, destination)</c> key: how many flights have gone there, and the
    /// unscaled time of the most recent one.</summary>
    private static readonly Dictionary<string, int> s_count = new(8);
    private static readonly Dictionary<string, float> s_last = new(8);

    /// <summary>
    /// Record and announce one pile flight.
    /// </summary>
    /// <param name="board">Whose board the flight is on — <c>"own"</c> for this client's own card,
    /// <c>"peer N"</c> for a mirrored one. Half the key.</param>
    /// <param name="destination">The stack the card is flying INTO (<c>Burnt</c>, <c>Discard</c>,
    /// …). The other half of the key.</param>
    /// <param name="trigger">The GAME EVENT that started this flight, in the producer's own words
    /// — see the call sites for the vocabulary. Two lines sharing a key with DIFFERENT triggers is
    /// the duplicate signature.</param>
    /// <param name="cardName">The card, when the producer knows it; <c>null</c> for the anonymous
    /// mirrored slab, which carries no identity by design.</param>
    /// <param name="resolvedTo">Where the flight was REALLY aimed, named by the stack its endpoint
    /// is nearest to, for the producers that resolve a world point (see the class doc's item 4c
    /// paragraph). OPTIONAL and defaulted so the producers that fly to a pose they did not compute
    /// — every own-board row, which hands <c>VRCard</c> a pile transform it owns — keep the call
    /// they had; a null simply prints "not resolved by this producer" and claims nothing.</param>
    internal static void Note(string board, string destination, string trigger, string? cardName,
        string? resolvedTo = null)
    {
        string key = board + " -> " + destination;
        s_count.TryGetValue(key, out int n);
        n++;
        s_count[key] = n;

        float now = Time.unscaledTime;
        float gap = s_last.TryGetValue(key, out float prev) ? now - prev : -1f;
        s_last[key] = now;

        // HW-VERIFY: report items 5 and 7 — the per-flight trigger/repeat reading. Grep token:
        // PILE FLIGHT. See this class's doc for WORKING / INERT / STILL BEYOND THE INSTRUMENT.
        VRLog.Note("Cards", $"PILE FLIGHT [{board}]: flight #{n} into the {destination} stack, "
            + $"trigger '{trigger}', card '{cardName ?? "(anonymous mirrored slab — no identity on the wire)"}', "
            + $"game phase {PhaseName()}, "
            + (gap < 0f
                ? "and it is the FIRST flight to this stack on this board this session."
                : $"{gap:F2}s after the previous flight to this stack on this board.")
            + (resolvedTo == null
                ? " AIMED AT: not resolved by this producer — it flies to a pile transform it owns "
                  + "rather than to a world point it computed, so label and endpoint cannot disagree."
                : $" AIMED AT: {resolvedTo}. Read this against the destination above — they name "
                  + "the same flight from the two ends that can disagree (the label the producer "
                  + "intended, and the stack the endpoint is really nearest). A MISMATCH is report "
                  + "item 4c, 'die Fluganimation beider Karten ging in den Verbrannt Stapel beim "
                  + "remote board', named outright.")
            + " THE KEY IS (board, destination) AND NOT THE CARD NAME, because the mirrored wire "
            + "flight draws an anonymous slab and has no name to match on. A second '-> Burnt' "
            + "line for the same board with a DIFFERENT trigger and a small gap is report item 5: "
            + "two producers flying one burn. Repeats on a '-> Discard' key are legitimate — a "
            + "discarded card is drawn and discarded again — so read the gap, not the ordinal.");
    }

    /// <summary>The game's own phase, for the "at what point in the turn" half of the reading. A
    /// static enum read; guarded because a game-side shape change must never throw out of a card
    /// animation.</summary>
    private static string PhaseName()
    {
        try
        {
            return PhaseManager.PhaseType.ToString();
        }
        catch
        {
            return "?";
        }
    }

    /// <summary>Forget everything (driver teardown / scenario change) so one scenario's ordinals
    /// never accuse the next one's first flight of being a repeat.</summary>
    internal static void Reset()
    {
        s_count.Clear();
        s_last.Clear();
    }
}
