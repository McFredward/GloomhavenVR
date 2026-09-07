using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE ONE PLACE the game's own action-region highlight (<c>CardActionHighlight</c>) is asserted
/// onto a card, and the one place that says — in seconds — how fast it is actually pulsing.
///
/// <para>USER ITEM 7 (2026-09-07, verbatim): "Die Frequenz in dem der aktive Bereich blinkt bei den
/// aktiven Karten war plötzlich viel höher als zuvor. Es soll mit der dauerhaft selben ruhigen
/// Frequenz blinken."</para>
///
/// <para>"PLÖTZLICH" AND "VIEL HÖHER" EXCLUDE A WRONG CONSTANT. A wrong constant is wrong from the
/// first frame. A rate that rises DURING a session is a rate with a term in it that varies during a
/// session — and there is exactly one such term on this surface.</para>
///
/// <para>THE AUTHORED PULSE IS NOT OURS AND IS NOT A CONSTANT WE CHOSE. The game frames an action
/// region with <c>CardActionHighlight</c>, and <c>ShowHover()</c> runs a self-re-entrant LeanTween
/// chain — <c>LoopHighlight(fromAlfa, toAlfa)</c>, <c>hoverDuration</c> per leg, each leg's
/// <c>setOnComplete</c> starting the next in the opposite direction (CardActionHighlight.cs:37-52).
/// The shipped serialized values are <c>fromAlfa 1</c>, <c>toAlfa 0.3</c>, <c>hoverDuration 0.5</c>,
/// so one bright-dim-bright cycle is TWO legs = 1.0 s. That number is "die ruhige Frequenz", and
/// this class reads it off the live component rather than restating it.</para>
///
/// <para>AND <c>ShowHover()</c> IS NOT IDEMPOTENT. Its first two statements are
/// <c>CancelAnimation()</c> and, inside <c>LoopHighlight</c>, <c>canvasGroup.alpha = from</c> — so
/// every call KILLS the running chain, SNAPS the alpha back to 1 and starts a fresh 0.5 s leg. Call
/// it again after <c>T</c> seconds and the eye never sees a leg finish: the visible cycle is
/// <c>T</c>, not 1.0 s, and the "frequency" is <c>1/T</c> — the caller's cadence, wearing the
/// pulse's clothes. Nothing about that is a constant.</para>
///
/// <para>THE MIRROR ALREADY KNEW. <c>Net.RemoteBoardCard.ApplyHalf</c> has gated this write since
/// the day it was written and its own comment names the symptom exactly — "A hover that is already
/// running is left alone, so the game's LeanTween loop is never restarted mid-cycle (which would
/// visibly re-snap the alpha to 1 every frame)". That path is driven from a per-frame board tick,
/// so it could not have shipped any other way. The LOCAL twin —
/// <c>CardsDriver.SetActiveHighlight</c>, the active column's own highlight — has never had the
/// gate: it calls <c>FullAbilityCard.ToggleHighlightHover(active: true, …)</c> unconditionally on
/// every <c>UpdateActive</c>, i.e. on every <c>Rebuild</c>. <c>Rebuild</c> runs on <c>_dirty</c>
/// alone and there are 49 places that raise it, several of them per-frame watchdogs. So the local
/// active column's blink period IS the driver's rebuild interval whenever that interval is shorter
/// than a 0.5 s leg — a term that is low while the board is idle and high while anything is
/// happening. That is the whole of "plötzlich viel höher als zuvor", and it is a gate, not a dial.
/// </para>
///
/// <para>SO THE GATE MOVED HERE INSTEAD OF BEING COPIED. Two surfaces asserting the same game
/// component must not be able to hold two opinions about when a write is owed — the mirror's own
/// integrator note makes that point about a different pair in the same file. The expression below
/// IS the mirror's, verbatim in behaviour, and both callers now ask it.</para>
///
/// <para>THE SELF-HEAL TERM IS LOAD-BEARING AND MUST NOT BE SIMPLIFIED AWAY. The gate is on the
/// wanted state AND on the highlight object's REAL <c>activeSelf</c>, because the card's own
/// <c>FullAbilityCardAction.OnEnable -> Show() -> highlight.Hide()</c> can take the highlight down
/// underneath us. A purely state-gated driver would go dark for good the first time that happened —
/// and the local path's ONLY protection against it today is the very re-assert that causes this
/// defect, so removing one without the other would trade a fast blink for no blink at all.</para>
/// </summary>
internal static class ActionHighlightDriver
{
    private const string Scope = "Cards";

    /// <summary>Wanted state for one half: nothing lit.</summary>
    internal const int Off = -1;

    /// <summary>Wanted state for one half: the game's pulsing hover look.</summary>
    internal const int Hover = 0;

    /// <summary>Wanted state for one half: the game's steady committed look.</summary>
    internal const int Selected = 1;

    /// <summary>Which surface asked — the census reports the two separately, because the whole
    /// point of item 7 is that one of them was gated and the other was not.</summary>
    internal enum Site
    {
        /// <summary>The owner's own ACTIVE-cards column (<c>CardsDriver.SetActiveHighlight</c>).</summary>
        ActiveColumn = 0,

        /// <summary>A peer's mirrored round-card recess (<c>Net.RemoteBoardCard.ApplyHalf</c>).</summary>
        MirroredRecess = 1,

        /// <summary>A peer's mirrored ACTIVE-card matrix (<c>Net.RemoteActiveCardPulse</c>) — user
        /// item 3 of 2026-09-07, the surface that had no driver at all until that round.</summary>
        MirroredActiveMatrix = 2,
    }

    private const int SiteCount = 3;

    // ------------------------------------------------------------------ the assert --

    /// <summary>
    /// Assert one half's wanted highlight state, restarting the game's own pulse ONLY on a real
    /// change. Returns false when this half carries no highlight object at all (a card prefab
    /// variant without one), which is the caller's signal to fall back to whatever it has.
    ///
    /// <para><paramref name="appliedState"/> / <paramref name="appliedRegion"/> are the CALLER's
    /// per-half memory and are updated in place; they are deliberately not held here, because the
    /// two callers have different lifetimes (a VR card outlives a mirrored slot's clone) and a
    /// static registry keyed on an instance id would be a third thing to prune.</para>
    /// </summary>
    /// <param name="action">The half's action widget (top or bottom).</param>
    /// <param name="want"><see cref="Off"/> / <see cref="Hover"/> / <see cref="Selected"/>.</param>
    /// <param name="wantDefault">true = the small standard-action chip's region, false = the big half's.</param>
    /// <param name="appliedState">The caller's memory of the last state pushed for this half.</param>
    /// <param name="appliedRegion">The caller's memory of the last REGION pushed for this half.</param>
    /// <param name="site">Which surface is asking (census attribution only).</param>
    internal static bool Assert(FullAbilityCardAction? action, int want, bool wantDefault,
                                ref int appliedState, ref bool appliedRegion, Site site)
    {
        CardActionHighlight? big = action != null ? action.highlightAction : null;
        CardActionHighlight? chip = action != null ? action.highlightDefaultAction : null;
        if (big == null && chip == null)
            return false;

        // A card prefab without the chip's highlight cannot show a chip glow. Falling back to the
        // BIG half's highlight there would light a whole half for a standard action, so it falls
        // back to NOTHING: less than the owner sees, never something different.
        CardActionHighlight? target = want < 0 ? null : wantDefault ? chip : big;

        // The region can flip while the STATE holds (the beam slides off the half onto its chip:
        // still "hover", different rectangle), so the gate is on BOTH. `isOn` is read from the
        // object we are about to write — that is what keeps the self-heal against the card's own
        // FullAbilityCardAction.OnEnable -> Show() -> highlight.Hide().
        bool wantOn = target != null;
        bool isOn = target != null && target.gameObject.activeSelf;
        bool gated = want == appliedState && wantDefault == appliedRegion && wantOn == isOn;

        s_seen[(int)site] = true;
        if (gated)
        {
            s_gated[(int)site]++;
        }
        else
        {
            appliedState = want;
            appliedRegion = wantDefault;
            if (target != null)
            {
                if (want == Selected)
                    target.ShowSelected(); // steady, selectedShineWidth — the committed region
                else
                    target.ShowHover();    // the game's own 1<->0.3 LeanTween loop, hoverShineWidth
                NoteRestart(target, site, want == Selected);
            }
        }

        // NEVER BOTH AT ONCE, and never a leftover: the same exclusivity
        // FullAbilityCardAction.RefreshHighlight keeps between its two highlights on the owner's own
        // card. Run unconditionally (not only on a change) because the object that has to go dark is
        // the one the gate above is NOT watching — a region flip and a hover ending are both cases
        // where the previously lit highlight would otherwise stay up.
        Park(want < 0 || !ReferenceEquals(big, target) ? big : null);
        Park(want < 0 || !ReferenceEquals(chip, target) ? chip : null);

        NoteDrivers(big, chip);
        MaybeEmitCensus();
        return true;

        static void Park(CardActionHighlight? hl)
        {
            if (hl != null && hl.gameObject.activeSelf)
                hl.Hide();
        }
    }

    // ------------------------------------------------------------------ the census --
    //
    // WHAT WAS MISSING, AND THE ABSENCE IS THE FIRST FINDING OF THIS ROUND. The prop overlays have
    // counted their own class of this defect since ModBuild 470 ("OVERLAY PULSES alive at once,
    // high-water", which read 1 in the 2026-09-07 host log). The card action highlight — the pulse
    // the user is actually complaining about — had NO instrument of any kind: not the period, not
    // the driver count, not the restart rate. Both logs of the ModBuild 474 session carry 102
    // `] [Cards] ACTIVE SET` lines saying WHICH cards are active and not one line saying how fast
    // their active region blinks. "Ruhig" is not a number and neither is "viel höher"; a period in
    // seconds is.

    /// <summary>Seconds between two census lines. Matched to <c>ACTIVE SET</c>'s re-statement
    /// cadence so the two can be read side by side in one log.</summary>
    private const float CensusIntervalSeconds = 15f;

    /// <summary>Restarts issued this window, per <see cref="Site"/>.</summary>
    private static readonly int[] s_restarts = new int[SiteCount];

    /// <summary>Has this site EVER asked this driver, since the module came up? NOT per-window, and
    /// it is the guard against the worst reading this line could give. The local half of item 7 is a
    /// change inside <c>CardsDriver.SetActiveHighlight</c> — a file this lane does not own — so
    /// until that lands the OWN ACTIVE COLUMN calls <c>FullAbilityCard.ToggleHighlightHover</c>
    /// directly and its restarts are invisible here. A census printing "own column restarted 0
    /// time(s)" would then read as the fix WORKING when in fact nothing on that surface is routed
    /// through the gate at all. This flag makes the line say which of the two it is.</summary>
    private static readonly bool[] s_seen = new bool[SiteCount];

    /// <summary>Asserts the gate REFUSED this window, per <see cref="Site"/> — the writes that did
    /// not happen. A window with restarts and no gated asserts is a caller with no steady state.</summary>
    private static readonly int[] s_gated = new int[SiteCount];

    /// <summary>Restarts that were <c>ShowSelected</c> (steady) rather than <c>ShowHover</c>
    /// (pulsing) — a steady look has no period to ruin, so it must not be read as a fast blink.</summary>
    private static readonly int[] s_steadyRestarts = new int[SiteCount];

    /// <summary>Shortest observed gap between two consecutive PULSE restarts on the SAME highlight
    /// object this window, in seconds. This is the delivered period: the number the eye reads.
    /// Infinity = no object was restarted twice, i.e. the game's own loop owned the phase.</summary>
    private static float s_worstDeliveredSeconds = float.PositiveInfinity;

    /// <summary>Which site produced <see cref="s_worstDeliveredSeconds"/>.</summary>
    private static Site s_worstSite = Site.ActiveColumn;

    /// <summary>Last pulse-restart time per highlight object instance id — the only state the
    /// delivered period can be measured from. Capped and cleared each window, so a scenario's worth
    /// of destroyed clones cannot accumulate.</summary>
    private static readonly Dictionary<int, float> s_lastRestart = new(64);

    /// <summary>Highest number of <c>CardActionHighlight</c>s drawn AT ONCE on one half this
    /// window. 2 means the big region and the standard-action chip are both lit on the same half —
    /// two pulses on one rectangle, which the eye sums into a beat rather than a blink.</summary>
    private static int s_driversHighWater;

    /// <summary>The authored leg length in seconds, read off the first live component seen
    /// (publicized serialized field, never guessed). 0 = not read yet.</summary>
    private static float s_authoredLegSeconds;

    /// <summary>The authored alpha range, read off the same component — the AMPLITUDE, which is the
    /// term this defect must be corrected at if it is ever corrected at all (recorded finding: an
    /// element strength may never scale a FREQUENCY).</summary>
    private static float s_authoredFromAlpha;

    /// <summary>The authored pulse's dim end — see <see cref="s_authoredFromAlpha"/>.</summary>
    private static float s_authoredToAlpha;

    private static float s_nextCensus;
    private static float s_windowOpened;

    /// <summary>Change gate: the delivered-period BUCKET last reported. A period that has not
    /// changed bucket re-states on the 15 s cadence; a period that has changed bucket prints at
    /// once, because "plötzlich" is precisely a bucket change.</summary>
    private static int s_loggedBucket = int.MinValue;

    /// <summary>The site's name as the census prints it — one expression, so a new site cannot be
    /// added to the enum and silently print as an older one's label.</summary>
    private static string SiteName(Site site) => site switch
    {
        Site.ActiveColumn => "OWN ACTIVE COLUMN",
        Site.MirroredRecess => "MIRRORED RECESS",
        _ => "MIRRORED ACTIVE MATRIX",
    };

    /// <summary>Record one pulse restart and the gap since this object's previous one.</summary>
    private static void NoteRestart(CardActionHighlight target, Site site, bool steady)
    {
        s_restarts[(int)site]++;
        if (steady)
        {
            s_steadyRestarts[(int)site]++;
            return; // ShowSelected does not loop: it has no period to shorten
        }

        ReadAuthored(target);

        int key = target.GetInstanceID();
        float now = Time.unscaledTime;
        if (s_lastRestart.TryGetValue(key, out float previous))
        {
            float gap = now - previous;
            if (gap > 0f && gap < s_worstDeliveredSeconds)
            {
                s_worstDeliveredSeconds = gap;
                s_worstSite = site;
            }
        }
        // The cap is a guard against a scenario's worth of destroyed clones, not a sample size: the
        // map is cleared every window anyway, and 256 distinct highlight objects inside 15 s is far
        // past any real board.
        if (s_lastRestart.Count < 256 || s_lastRestart.ContainsKey(key))
            s_lastRestart[key] = now;
    }

    /// <summary>Read the authored pulse off the live component ONCE — the number the user calls
    /// "ruhig", measured rather than restated.</summary>
    private static void ReadAuthored(CardActionHighlight target)
    {
        if (s_authoredLegSeconds > 0f)
            return;
        s_authoredLegSeconds = target.hoverDuration;
        s_authoredFromAlpha = target.fromAlfa;
        s_authoredToAlpha = target.toAlfa;
    }

    /// <summary>High-water of simultaneously-lit highlights on ONE half.</summary>
    private static void NoteDrivers(CardActionHighlight? big, CardActionHighlight? chip)
    {
        int live = 0;
        if (big != null && big.gameObject.activeSelf)
            live++;
        if (chip != null && chip.gameObject.activeSelf)
            live++;
        if (live > s_driversHighWater)
            s_driversHighWater = live;
    }

    /// <summary>Coarse bucket of the delivered period, so the change gate fires on a rate CHANGE
    /// and not on measurement noise: 0 = no restart pair at all (the game owns the phase),
    /// 1 = at or above one authored cycle, 2 = inside a cycle but at or above a leg,
    /// 3 = inside a leg, 4 = five times the authored rate or worse.</summary>
    private static int DeliveredBucket(float leg)
    {
        if (float.IsPositiveInfinity(s_worstDeliveredSeconds))
            return 0;
        if (leg <= 0f)
            return 1;
        if (s_worstDeliveredSeconds >= leg * 2f)
            return 1;
        if (s_worstDeliveredSeconds >= leg)
            return 2;
        if (s_worstDeliveredSeconds >= leg * 0.4f)
            return 3;
        return 4;
    }

    private static void MaybeEmitCensus()
    {
        float now = Time.unscaledTime;
        if (s_windowOpened <= 0f)
            s_windowOpened = now;
        float leg = s_authoredLegSeconds;
        int bucket = DeliveredBucket(leg);
        // ONLY A WORSENING BUCKET JUMPS THE CADENCE. `!=` here would ping-pong: emitting resets the
        // window, the next window starts with no restart pair at all (bucket 0), and a `!=` test
        // would fire again immediately, then again on the next real restart — an unbounded burst
        // from a line that is supposed to be one every 15 s. "Plötzlich" is a rate going UP, which
        // is the only direction worth interrupting the cadence for; a rate coming back down is news
        // that can wait 15 s.
        if (now < s_nextCensus && bucket <= s_loggedBucket)
            return;

        int columnRestarts = s_restarts[(int)Site.ActiveColumn];
        int mirrorRestarts = s_restarts[(int)Site.MirroredRecess];
        int matrixRestarts = s_restarts[(int)Site.MirroredActiveMatrix];
        int columnGated = s_gated[(int)Site.ActiveColumn];
        int mirrorGated = s_gated[(int)Site.MirroredRecess];
        int matrixGated = s_gated[(int)Site.MirroredActiveMatrix];
        if (columnRestarts + mirrorRestarts + matrixRestarts
            + columnGated + mirrorGated + matrixGated == 0)
        {
            s_nextCensus = now + CensusIntervalSeconds; // nothing asserted at all: no reading to give
            s_windowOpened = now;
            return;
        }

        float window = Mathf.Max(0.001f, now - s_windowOpened);
        string delivered = float.IsPositiveInfinity(s_worstDeliveredSeconds)
            ? "NOT SHORTENED (no highlight object was restarted twice this window, so the game's own "
              + "loop owned the phase end to end)"
            : $"{s_worstDeliveredSeconds:F3} s, i.e. {1f / s_worstDeliveredSeconds:F2} Hz, worst on "
              + $"the {SiteName(s_worstSite)}";
        string authored = leg > 0f
            ? $"{leg * 2f:F2} s ({leg:F2} s per leg x 2, alpha {s_authoredFromAlpha:F2} to "
              + $"{s_authoredToAlpha:F2}), read off the live CardActionHighlight"
            : "not read yet (no pulse restart has happened, so no component has been sampled)";
        int steady = s_steadyRestarts[(int)Site.ActiveColumn] + s_steadyRestarts[(int)Site.MirroredRecess]
                     + s_steadyRestarts[(int)Site.MirroredActiveMatrix];

        // HW-VERIFY: ITEM 7's whole answer, and the instrument the active column has never had.
        // WORKING = 'DELIVERED PERIOD NOT SHORTENED' on every line of the session, with the own
        // column's restart count at 0..2 and its gated count in the hundreds: the game's LeanTween
        // chain runs uninterrupted and the blink is the authored 1.00 s. DEFECTIVE = any line whose
        // DELIVERED PERIOD prints a number — that number IS the period the user is seeing, in
        // seconds, its reciprocal IS the frequency in Hz, and the site clause names which of the two
        // surfaces shortened it. STILL BEYOND THE INSTRUMENT = the user reports a fast blink while
        // every line reads NOT SHORTENED and 'drivers on one half, high-water: 1' — then the pulse
        // is being restarted by a writer that is not this class (the game's own
        // CardsActionControlller.RefreshHighlight, or FullCardEventPusher's mouse-over route) and
        // the next round starts from that reading rather than from this class.
        VRLog.Note(Scope, "ACTION HIGHLIGHT PULSE (change-triggered on the delivered-period bucket, "
            + $"re-stated every {CensusIntervalSeconds:F0} s; frame {Time.frameCount}): "
            + $"AUTHORED PERIOD {authored}. DELIVERED PERIOD {delivered}. "
            + "PHASE SOURCE: the game's own CardActionHighlight LeanTween chain, restarted "
            + $"{columnRestarts} time(s) by the OWN ACTIVE COLUMN, {mirrorRestarts} time(s) by the "
            + $"MIRRORED RECESS and {matrixRestarts} time(s) by the MIRRORED ACTIVE MATRIX over the "
            + $"last {window:F1} s ({steady} of those were the STEADY "
            + "ShowSelected look, which has no period to shorten); the gate refused "
            + $"{columnGated} own-column, {mirrorGated} mirrored-recess and {matrixGated} "
            + "mirrored-matrix assert(s) in the same window. "
            + $"DRIVERS ON ONE HALF, high-water: {s_driversHighWater} (2 = the big action region and "
            + "its standard-action chip are lit at once on the same half, so the eye sums two "
            + "pulses; the game's own RefreshHighlight keeps exactly one). READ THIS AS A RATE, NOT "
            + "AN ADJECTIVE: ShowHover is not idempotent — it cancels the running chain and re-seeds "
            + "canvasGroup.alpha to the FROM value (CardActionHighlight.cs:37-52) — so a caller that "
            + "re-asserts every T seconds makes the visible cycle T and the frequency 1/T, whatever "
            + "the authored period says. That is why the delivered number above answers "
            + "'ploetzlich viel hoeher' and the authored one does not. "
            + "ROUTED THROUGH THIS GATE: own active column="
            + (s_seen[(int)Site.ActiveColumn] ? "YES" : "NO — READ THE ZERO ABOVE AS 'NOT MEASURED', "
                + "NOT AS 'NOT HAPPENING'. CardsDriver.SetActiveHighlight is still calling "
                + "FullAbilityCard.ToggleHighlightHover directly, so the owner's own active column "
                + "is ungated and its restarts are counted nowhere. Item 7 is NOT fixed on that "
                + "surface until that method's body routes through VRCard.SetActionHighlight")
            + $", mirrored recess={(s_seen[(int)Site.MirroredRecess] ? "YES" : "NO — no peer board "
                + "has drawn a real card face this session, so that column of numbers is empty by "
                + "absence of a subject, not by a gate")}"
            + $", mirrored active matrix={(s_seen[(int)Site.MirroredActiveMatrix] ? "YES" : "NO — no "
                + "peer's ACTIVE matrix has hosted a real card face this session (nobody has had an "
                + "active card while their board was mirrored), so item 3's own numbers are empty by "
                + "absence of a subject rather than by a gate; a peer WITH an active card and this "
                + "still reading NO means Net.RemoteActiveCardPulse never found the hosted "
                + "FullAbilityCard clone, which is the finding")}.");

        s_loggedBucket = bucket;
        s_nextCensus = now + CensusIntervalSeconds;
        s_windowOpened = now;
        for (int i = 0; i < SiteCount; i++)
        {
            s_restarts[i] = 0;
            s_gated[i] = 0;
            s_steadyRestarts[i] = 0;
        }
        s_worstDeliveredSeconds = float.PositiveInfinity;
        s_driversHighWater = 0;
        s_lastRestart.Clear();
    }

    /// <summary>Drop every window tally and the authored reading — a scenario teardown must not
    /// carry the previous one's period into the next one's first line.</summary>
    internal static void Reset()
    {
        for (int i = 0; i < SiteCount; i++)
        {
            s_restarts[i] = 0;
            s_gated[i] = 0;
            s_steadyRestarts[i] = 0;
        }
        s_worstDeliveredSeconds = float.PositiveInfinity;
        s_driversHighWater = 0;
        s_lastRestart.Clear();
        s_authoredLegSeconds = 0f;
        s_authoredFromAlpha = 0f;
        s_authoredToAlpha = 0f;
        s_nextCensus = 0f;
        s_windowOpened = 0f;
        s_loggedBucket = int.MinValue;
        for (int i = 0; i < SiteCount; i++)
            s_seen[i] = false;
    }
}
