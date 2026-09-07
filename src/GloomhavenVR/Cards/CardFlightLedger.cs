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
    internal static void Note(string board, string destination, string trigger, string? cardName)
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
