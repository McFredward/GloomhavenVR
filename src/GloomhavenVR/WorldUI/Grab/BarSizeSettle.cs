using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>A CHANGE IN A GRAB BAR'S SIZE HAS TO BE EARNED.</b> One scalar in, one scalar out: the value
/// returned is the last one the live input HELD for <see cref="SettleSeconds"/>, so a term that
/// moves and moves back inside that window never reaches the rod at all.
///
/// <para><b>THE REPORT (user, 2026-09-03, verbatim):</b> <i>"Das Entscheidungsfenster hat nun einen
/// Greifbalken und lässt sich verschieben. Wählt man einen Character nun aus, kommt es nun für eine
/// Sekunde oder kürzer dazu, dass der Greifbalken größer wird und dann direkt wieder kleiner. Sowas
/// möchte ich gerne allgemein unterdrücken. Wenn sich das Fenster nicht wirklich vergrößert, sollte
/// der Greifbalken auch nicht größer werden."</i> Two things in that are requirements rather than a
/// bug report. <b>"Allgemein"</b> — generally, not on the one panel he happened to be looking at —
/// is why this is a shared class and not a field on <c>SurfaceGrabBar</c>: both owners of the bar
/// machinery (<see cref="GrabbableModal"/> and <c>SurfaceGrabBar</c>) run their size terms through
/// it, so the two handles cannot drift in behaviour any more than they may drift in look. And
/// <b>"wenn sich das Fenster nicht wirklich vergrößert"</b> is the rule itself, stated in the user's
/// own words: <i>really</i> bigger means bigger and STAYING bigger.</para>
///
/// <para><b>WHY A BAR CAN GROW WHILE ITS WINDOW DOES NOT.</b> The rod's drawn length is a fraction
/// of the panel's measured width, and the panel's measured width is the mod-owned HOST RECT — not
/// the game graphic the player sees. On the reward popup those two are demonstrably different
/// objects: the ModBuild 375 log has <c>HIT RECT 'GloomhavenVR.Panel_DistributeReward' … host rect
/// 368x500 px … DRAWN CONTENT 416x464 px … LARGER = the content</c>, i.e. the visible popup
/// OVERFLOWS its own host rect and is not stretched to it. So the host rect can be re-fitted under
/// the popup without one visible pixel of the popup moving — and the bar, which reads the host rect,
/// follows the invisible change. That is exactly the shape of the complaint: the handle changed
/// size, the window did not. The same seam exists on the modal side, where the fit machinery has
/// been logged writing a window's host width twice in a few frames (<c>Host rect fit
/// '…UI Event Window': 1920x1080 → 802x126</c> then <c>802x126 → 1544x792</c> — a 1.9x width swing
/// on one window inside one interaction).</para>
///
/// <para><b>WHAT THIS DELIBERATELY DOES NOT GATE.</b> POSITION. The bar's x/y still track the live
/// rect every frame, because a handle that lags its own window is a handle hanging off the side of
/// it — a worse artefact than the one being fixed, and not the one reported. VISIBILITY: ModBuild
/// 243 shipped an explicit user ruling that the handle must come off an empty window
/// <i>instantly</i> (<i>"ich möchte gerne dass es instant reagiert"</i>), and
/// <c>GrabbableModal.SyncBarVisibility</c> is untouched by anything here. And the TWO-HAND RESIZE,
/// which is not a rect change at all: <see cref="PanelGrabHandle"/> writes the GRAB ROOT's
/// <c>localScale</c> (PanelGrab.cs:904), the rod is a child of that root, and so the pinch keeps
/// scaling the whole assembly — window and handle together, at the frame rate, through a term that
/// never enters this class.</para>
/// </summary>
internal sealed class BarSizeSettle
{
    /// <summary>
    /// How long a new value must stand before the bar adopts it.
    ///
    /// <para><b>DERIVED FROM THE REPORT'S OWN UPPER BOUND.</b> The user timed the artefact himself:
    /// <i>"für eine Sekunde oder kürzer"</i> — at most one second, and the operative half of that
    /// phrase is "oder kürzer". A settle window equal to his ceiling therefore suppresses every
    /// excursion he described, and there is no smaller number that can be justified from evidence
    /// this session has: the ModBuild 375 log records exactly ONE <c>Host rect fit</c> for the panel
    /// in question, so the true duration of the transient has never been measured. It is measured
    /// now — the <c>GRAB BAR SIZE</c> line below prints how long each excursion actually stood, and
    /// the first hardware round that produces one is what licenses tuning this down.</para>
    ///
    /// <para><b>WHAT A SECOND COSTS.</b> Only a genuine, persistent size change arrives late, and
    /// only by this much. It cannot delay a drag (position is ungated), it cannot delay the two-hand
    /// resize (that moves <c>localScale</c>, never the rect), and it cannot delay the handle coming
    /// off an empty window (a separate rule). What remains is a window whose CONTENT really got
    /// wider: its rod widens a second later, which reads as settling rather than as lag, because
    /// nothing about the gesture that caused it is still in the player's hand.</para>
    /// </summary>
    internal const float SettleSeconds = 1.0f;

    /// <summary>
    /// Two values count as the same size within this fraction of the larger. RELATIVE and not
    /// absolute because the terms handed in are not in one unit: the width arrives in frame-local
    /// metres at the diorama's scale (~51 world units for the reward popup at 198 units per real
    /// metre) and the height in real metres (~0.35). One percent is far below anything the eye can
    /// resolve on a rod and far below every real movement on record — the reward popup's own frame
    /// is 368 px against a 416 px content union (13 %), and the event window's logged width swing
    /// was 92 %.
    /// </summary>
    private const float RelativeTolerance = 0.01f;

    /// <summary>
    /// Floor on the interval between two printed lines FROM THE SAME SETTLER. A term that jitters
    /// every frame would otherwise print an excursion edge every frame; the counts are still
    /// incremented for every event, so a throttled event is delayed in the log, never lost.
    /// </summary>
    private const float ReportThrottleSeconds = 1.5f;

    /// <summary>Which term this settler owns, for the log line. Never null.</summary>
    private readonly string _term;

    /// <summary>The value in force — what the bar is actually drawn from.</summary>
    private float _settled;

    /// <summary>False until the first sample. THE FIRST VALUE IS ADOPTED IMMEDIATELY: a bar must be
    /// the right size on the frame it appears, and there is nothing yet for a transient to be
    /// measured against.</summary>
    private bool _seeded;

    /// <summary>True while the live value is away from <see cref="_settled"/> — i.e. inside an
    /// excursion that has not yet been earned or abandoned.</summary>
    private bool _away;

    /// <summary>The value currently being timed, and when it was first seen. A DIFFERENT value
    /// restarts this clock, so a term that is still moving never commits mid-flight.</summary>
    private float _candidate;
    private float _candidateSince;

    /// <summary>When the live value first left <see cref="_settled"/>. This — and not
    /// <see cref="_candidateSince"/> — is what "how long the transient stood" means.</summary>
    private float _leftAt;

    /// <summary>The most extreme value seen during the current excursion. This is what the PLAYER
    /// saw, so it is what the log reports; the candidate is only the bookkeeping.</summary>
    private float _peak;

    /// <summary>Running over this settler's life. Both are printed on every line, so a change-gated
    /// instrument whose reason never varies still says how often it fired.</summary>
    private int _suppressed;
    private int _committed;

    /// <summary>Last print, and whether anything has been printed at all.</summary>
    private float _lastReport;
    private bool _reported;

    internal BarSizeSettle(string term)
    {
        _term = term;
    }

    /// <summary>
    /// Feed the live term and take back the settled one. Allocation-free on every tick that does
    /// not print; called once per bar per frame from both owners' <c>SyncBar</c>.
    /// </summary>
    /// <param name="live">The term as measured this frame.</param>
    /// <param name="onScreen">Whether the rod is actually being drawn — see the OFF-SCREEN CHANGES
    /// ARE FREE block below. Pass the owner's own "is this handle visible" answer, never a
    /// constant.</param>
    /// <param name="logName">The window's log name, for the instrument.</param>
    internal float Apply(float live, bool onScreen, string logName)
    {
        if (!_seeded)
        {
            _seeded = true;
            _settled = live;
            return _settled;
        }

        // OFF-SCREEN CHANGES ARE FREE, AND GATING THEM WOULD RE-OPEN A CLOSED BUG. This rule exists
        // so the PLAYER does not see the handle change size for no reason; a change made while the
        // rod is not on the screen is not something he can see, so there is nothing to suppress and
        // holding the old value only makes the settled term untrue.
        //
        // It is not a nicety. Every floated window is built render-hidden and is fitted BEFORE the
        // reveal gate lets it through — the fit's own comment records why ("the reveal gate waits
        // for this fit now", CanvasConversion.3.Fit.cs, the 2026-08-02 post-reveal-jump ruling), and
        // the hardware logs show that first fit moving a host from 1920x1080 to 802x126. The bar is
        // seeded at the PRE-fit rect, so without this branch every window in the mod would reveal
        // with a rod sized for a 1920 px frame and snap to the real one a settle window later —
        // which is the exact "larger and lower, then snaps" artefact that ruling was written to
        // kill, moved from the window onto its handle.
        if (!onScreen)
        {
            _away = false;
            _settled = live;
            return _settled;
        }

        // UNSCALED, deliberately. Modal windows stand while the game is paused and a paused
        // Time.time would freeze this rule with a transient value in force.
        float now = Time.unscaledTime;

        if (Same(live, _settled))
        {
            if (_away)
            {
                _away = false;
                _suppressed++;
                Report(logName, from: _settled, to: _settled, saw: _peak, stood: now - _leftAt,
                       earned: false);
            }
            // Snap to the live value even inside the tolerance, so the settled term can never carry
            // a slow drift of sub-tolerance steps away from the truth it is supposed to be.
            _settled = live;
            return _settled;
        }

        if (!_away)
        {
            _away = true;
            _leftAt = now;
            _peak = live;
            _candidate = live;
            _candidateSince = now;
            return _settled;
        }

        if (Mathf.Abs(live - _settled) > Mathf.Abs(_peak - _settled))
            _peak = live;

        if (!Same(live, _candidate))
        {
            _candidate = live;
            _candidateSince = now;
            return _settled;
        }

        if (now - _candidateSince < SettleSeconds)
            return _settled;

        // EARNED. The value held for the whole window, so it is what the window really is.
        float previous = _settled;
        _settled = live;
        _away = false;
        _committed++;
        Report(logName, from: previous, to: _settled, saw: _peak, stood: now - _leftAt,
               earned: true);
        return _settled;
    }

    /// <summary>
    /// Forget everything. Called when a bar is built and when it is torn down, so a re-floated
    /// window can never inherit the size its previous holder settled on — the rect it is measured
    /// against is a different object by then, and a stale settled width would hold a wrong-sized rod
    /// on the screen for a whole settle window before the rule could correct it.
    /// </summary>
    internal void Reset()
    {
        _seeded = false;
        _away = false;
        _settled = 0f;
        _candidate = 0f;
        _candidateSince = 0f;
        _leftAt = 0f;
        _peak = 0f;
        _suppressed = 0;
        _committed = 0;
        _lastReport = 0f;
        _reported = false;
    }

    /// <summary>
    /// SYMMETRIC BY DESIGN, not by omission — the same tolerance decides "this is the same size"
    /// in both directions, so a shrink has to be earned exactly as a growth does.
    ///
    /// <para>The asymmetric alternative (gate growth, apply every shrink at once) was considered and
    /// is a NEW bug rather than a smaller fix. Under it the very same excursion the user reported,
    /// running the other way — a term that dips and comes back — is applied on the dip and then has
    /// to re-earn its way back up; a term that dips once a second never gets there at all, and the
    /// bar ratchets DOWN to the transient's floor and stays. It also fails the user's rule as he
    /// wrote it: <i>"wenn sich das Fenster nicht wirklich vergrößert"</i> is about the window not
    /// REALLY changing, and a size that reverts inside a second was not real in either direction.
    /// The cost of the symmetric choice is one settle window of a rod that is momentarily wider than
    /// the window it belongs to. That is a bar slightly too long for a second, against a bar
    /// permanently too short.</para>
    /// </summary>
    private static bool Same(float a, float b)
    {
        float scale = Mathf.Max(Mathf.Abs(a), Mathf.Abs(b), 1e-6f);
        return Mathf.Abs(a - b) <= RelativeTolerance * scale;
    }

    /// <summary>
    /// The one printed line. Change-gated on the EDGES of an excursion — it fires when a transient
    /// ends without being adopted, and when a value is adopted — never per frame, and never on a
    /// steady term.
    ///
    /// <para>It carries the running counts because a change-gated line whose reason never varies
    /// prints once and then reads exactly like a dead instrument. With the counts on it, one line
    /// still answers "how often": a session with a big <c>suppressed</c> and a small
    /// <c>committed</c> is the rule working; the reverse is a settle window that is too short; both
    /// at zero is a bar whose terms never move and a driver that is somewhere else entirely.</para>
    ///
    /// <para>It states only what it measured. It does NOT name a cause: this class can see that a
    /// number moved and came back, and cannot see whether the host rect, the ink union or the game's
    /// own layout moved it. The two terms are reported separately (<c>width</c> / <c>height</c>) so
    /// the log names WHICH of them the driver touched, which is the question the fix was built
    /// without an answer to.</para>
    /// </summary>
    private void Report(string logName, float from, float to, float saw, float stood, bool earned)
    {
        float now = Time.unscaledTime;
        if (_reported && now - _lastReport < ReportThrottleSeconds)
            return;
        _lastReport = now;
        _reported = true;

        float ratio = Mathf.Abs(from) > 1e-6f ? saw / from : 0f;
        string verdict = earned
            ? $"COMMITTED a change from {from:0.####} to {to:0.####}"
            : $"SUPPRESSED a transient — the settled value {to:0.####} was never replaced";

        // HW-VERIFY: the whole answer to "Sowas möchte ich gerne allgemein unterdrücken". A
        // SUPPRESSED line with a `stood` well under the settle window is the reported artefact being
        // caught; a COMMITTED line is a real resize still reaching the bar. If the user still sees
        // the handle flash and this line never appears for that window, the driver is NOT one of the
        // two terms below and the search moves to the frame's own localScale or the world scale.
        VRLog.Note("WorldUI", $"GRAB BAR SIZE '{logName}' [{_term}]: {verdict}. It saw "
                              + $"{saw:0.####} ({ratio:0.##}x the settled value) and that stood for "
                              + $"{stood * 1000f:0} ms against a {SettleSeconds * 1000f:0} ms settle "
                              + $"window. OVER THIS BAR'S LIFE: {_suppressed} transient(s) "
                              + $"suppressed, {_committed} change(s) committed. Both directions are "
                              + "gated the same way, so this is not a one-way ratchet; POSITION, "
                              + "VISIBILITY and the two-hand resize are ungated and are not "
                              + "described by this line.");
    }
}
