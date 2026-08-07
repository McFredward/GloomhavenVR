using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Plays the game's inter-round INITIATIVE REORDER slide to completion on the adopted
/// world-space track (user, hardware test: "Es sieht aber immer so aus, als ob die Animation
/// mitten drin geskipped/abgebrochen wird … Ich will, dass die Animation vollständig spielt und
/// man vollständig sieht, wie sich die Bildchen verschieben").
///
/// WHAT VANILLA DOES (read from source — decompiled/GH.Runtime/InitiativeTrack.cs:258-298,
/// <c>AnimateInitiativeReorder</c>):
/// <code>
///   isAnimating = true;
///   contentSizeFitter.enabled = false;  layoutGroup.enabled = false;
///   yield return new WaitForEndOfFrame();
///   foreach (entry in holder.GetComponentsInChildren&lt;InitiativeTrackActorBehaviour&gt;(false))
///       locations.Add(entry.transform.position.x);          // (1) WORLD x
///   UpdateSortingOrder(updateDisplay: true);                 // re-sort + SetAsFirstSibling
///   foreach (active entry i in actorsUI)
///       LeanTween.moveX(entry.gameObject, locations[n-1-i], trackReorderDuration)
///                .setEase(LeanTweenType.easeOutQuad);        // (2) tween WORLD x
///   yield return Timekeeper.instance.WaitForSeconds(trackReorderDuration);  // (3) CHRONOS clock
///   layoutGroup.enabled = true;  contentSizeFitter.enabled = true;          // (4) snap
/// </code>
///
/// TWO INDEPENDENT DEFECTS FALL OUT OF THAT ON A VR WORLD-SPACE PANEL, and both end the same
/// way — at (4) the layout group comes back and slams every portrait into its final slot, i.e.
/// exactly the "cut off half-way" the user sees:
///
/// <list type="number">
/// <item><b>The tween is WORLD-AXIS LOCKED (1)+(2).</b> On the game's screen-space overlay canvas
/// the row runs along world +X by construction (canvas units ARE pixels, no rotation), so
/// "slide along world x" and "slide along the row" are the same sentence. The mod's adopted panel
/// is a rotated, scaled world-space canvas docked on the play tray: the row's right axis is
/// whatever the tray's yaw makes it (fresh log: BL=(31.110,13.884,18.700) TR=(22.865,14.965,18.546)
/// — that session happened to land within a few degrees of −X, which is luck, not a guarantee;
/// the tray is player-grabbable and re-orientable). Off that axis, writing world x while holding
/// world y/z (LeanTween's <c>MOVE_X</c>) does NOT move a portrait along the row: the travel is
/// foreshortened by cos(yaw) AND the portrait leaves the panel plane. The slide then covers only
/// part of the distance (and part of it in DEPTH, where it reads as a wobble rather than a
/// sideways move) before (4) snaps it home — "abgebrochen" in the literal and in the
/// merely-invisible sense at once.</item>
/// <item><b>The animation window and the animation run on DIFFERENT CLOCKS (2)+(3).</b> The tween
/// is LeanTween, which integrates <c>Time.deltaTime</c> (LeanTween.cs:177,
/// <c>dtActual = Time.deltaTime</c>). The window that decides when the layout group comes back is
/// <c>Timekeeper.instance.WaitForSeconds</c>, which integrates the CHRONOS global clock
/// (Timekeeper.cs:96-104 → Clock.cs:174-178, <c>deltaTime = Time.unscaledDeltaTime * timeScale</c>)
/// — and <c>TimeManager</c> never touches <c>Time.timeScale</c>, it only writes
/// <c>Timekeeper.instance.m_GlobalClock.localTimeScale</c> (TimeManager.cs:34/86/103). So the two
/// clocks diverge by exactly the Chronos scale: with the game's speed-up toggle on
/// (<c>GlobalData.cs:635</c>: <c>TimeManager.DefaultTimeScale = GameSpeedIncreaseAmount</c>) the
/// 0.5 s window expires at 1/scale of the way through a 0.5 s tween, and with the clock PAUSED
/// (every <c>SmartFocus(pauseDuringTransition: true)</c>, which <c>InitiativeTrack.Select</c>
/// itself issues) it expires late instead. VR sits on the wrong side of that seam far more often
/// than flat play does, because the VR camera never "arrives" and the pause is released by the
/// mod's own <see cref="Board.CameraArrivalGuard"/> — see the fresh log's
/// "camera-follow transition completed instantly … TimeManager.IsPaused now False" landing
/// INSIDE the reorder window.</item>
/// </list>
///
/// THE FIX — while the track is adopted into world space, the MOD owns the slide:
/// <list type="bullet">
/// <item>cancel the game's world-x <c>LTDescr</c>s (its own <c>moveXAnimations</c> list, the same
///   handles <c>StopUpdateActors</c> cancels — a vanilla-reachable state, no new failure mode);</item>
/// <item>re-derive the identical destination assignment in the ROW's own local space (see
///   <see cref="Begin"/>) and slide <c>localPosition.x</c>, so the portraits travel along the row
///   at any panel orientation and can never leave the panel plane;</item>
/// <item>run it on <c>Time.unscaledDeltaTime</c> with the game's own
///   <c>trackReorderDuration</c> and its <c>easeOutQuad</c> shape, so neither the Chronos speed-up
///   nor a camera-transition pause can shorten or stretch what the eye sees;</item>
/// <item>hold <c>layoutGroup</c>/<c>contentSizeFitter</c> disabled until the slide is DONE —
///   re-asserted every frame, so when the game's own (Chronos-clocked) window closes early its
///   (4) can no longer snap the row mid-flight. Both components are switched back on at the end,
///   which is precisely the state vanilla's coroutine leaves behind.</item>
/// </list>
///
/// ─── ROUND 2 (MP hardware test 2026-08-08, PEER side): "die Bilder haben sich auf der Y-Achse
/// verschoben" + "ein Bildchen war gar nicht sichtbar; nur während der Animation blitzt es auf" ────
///
/// The cancel above is CORRECT but it is not, and cannot be, EARLY ENOUGH — and what leaks through
/// is not a wobble, it is a permanent off-axis displacement:
///
/// <list type="number">
/// <item><b>The leak (read from source).</b> Vanilla creates the tweens only after
/// <c>yield return new WaitForEndOfFrame()</c> (InitiativeTrack.cs:263), i.e. at the very END of
/// frame F, after rendering. LeanTween integrates them from an ordinary MonoBehaviour
/// <c>Update</c> (LeanTween.cs:154 → <c>update()</c>), so the FIRST world-x step lands in the
/// UPDATE phase of frame F+1 — strictly BEFORE this class's <c>LateUpdate</c> cancel can run.
/// Vanilla also has a SECOND, full-magnitude world-x writer that owes nothing to LeanTween at all:
/// <c>StopUpdateActors</c> (InitiativeTrack.cs:300-318, reached from the enemy-card animation at
/// :509) assigns <c>item.transform.position = new Vector3(locations[n-1-i], y, z)</c> — the entire
/// travel in one frame.</item>
/// <item><b>Why a world-x write becomes a Y STAIRCASE.</b> Writing world x while HOLDING world y/z
/// is only "along the row" when the row's right axis is world x. On the docked, tray-mounted panel
/// it is not: a world-x delta decomposes into (row travel, off-axis local y, out-of-plane local z),
/// and the off-axis part is PROPORTIONAL TO EACH ENTRY'S OWN TRAVEL — which is exactly a staircase.
/// Confirmed in the peer's log: the row's fitted union goes from
/// <c>(-588,-92)..(588,112)</c> (204 px tall, healthy) to <c>(-738,-167)..(608,167)</c> (334 px)
/// across one reorder, with two same-sized portrait rings measured at local y −146 and −26 in the
/// same frame (healthy frames have every ring at the same y). The local player's tray happened to
/// sit within a few degrees of the −X axis, where the off-axis component is ~0 — which is why the
/// SAME build looks perfect on one machine and staircased on the other.</item>
/// <item><b>Why it was PERMANENT, and why a portrait went missing.</b> The
/// <c>HorizontalLayoutGroup</c> rewrites a child's x and y on the next rebuild — but NOTHING in
/// uGUI ever rewrites a child's local Z. So the out-of-plane part of every leaked world-x write
/// ACCUMULATES on the entry root, round after round, until that portrait is far enough out of the
/// panel plane to project somewhere it cannot be seen — while still holding its layout slot (the
/// peer's screenshot shows the row's backdrop one slot wider than the visible portraits). During a
/// reorder the entry is dragged across the view again, which is the "flashes up briefly and is gone
/// again". The old code made this worse in one specific way: it sampled <c>RestY</c>/<c>RestZ</c>
/// (and <c>FromX</c>, and the destination slots) from the ALREADY-POLLUTED live transforms, so the
/// leaked offset was adopted as the row's rest pose and held for the whole slide.</item>
/// </list>
///
/// THE ROUND-2 FIX — the mod stops trusting the live transforms:
/// <list type="bullet">
/// <item><b>REST-POSE LATCH.</b> While the row is SETTLED (vanilla not animating, no slide) the row
///   is owned by the game's own layout group; that is the only state in which a transform's x/y/z
///   is trustworthy, so it is sampled there (<see cref="LatchRest"/>) and again on the RISING EDGE
///   of <c>isAnimating</c> — the coroutine's first segment, which has disabled the layout writers
///   but has not yet created a single tween. <see cref="Begin"/> builds the whole plan (FromX,
///   slots, RestY, RestZ) from that latch, so anything vanilla wrote in the gap is simply
///   discarded, and the slide's first frame REPAIRS it instead of preserving it.</item>
/// <item><b>ARM/FIRE INSTEAD OF AN ID KEY.</b> The old generation key was <c>batch[0].id</c>, which
///   MUTATES the moment we cancel: <c>removeTween</c> sets <c>counter = uint.MaxValue</c>
///   (LeanTween.cs:234) and <c>LTDescr.id</c> is <c>_id | (counter &lt;&lt; 16)</c>
///   (LTDescr.cs:139) — so a cancelled handle reports a different, NEGATIVE id and the next frame
///   read it as a brand-new batch. That is the duplicated "SLIDE start" in every hardware log, and
///   it re-ran the whole plan capture one frame later (i.e. one frame deeper into the pollution).
///   The trigger is now vanilla's own <c>isAnimating</c> rising edge, armed once and fired on the
///   first frame the tween batch exists.</item>
/// <item><b>CANCEL FIRST, ALWAYS.</b> The cancel moved ahead of every early return. A bail
///   ("nothing to animate", "fewer than two entries") used to leave vanilla's world-x tweens
///   RUNNING — the one state in which the full travel lands off-axis. On a world-space panel a
///   vanilla world-x tween is never correct, so there is no bail path that may leave one alive.
///   Bails are logged now, too: the peer's log had reorders with a "start" and no "done" and no
///   "aborted", which was this class going quiet.</item>
/// <item><b>ROW-AXIS GUARD.</b> Every idle frame (<see cref="GuardRowAxis"/>) the active row
///   entries are compared against the latch: an out-of-plane local z is written straight back (no
///   other component in the process owns it), and a grossly off-axis local y is restored AND the
///   holder is marked for layout rebuild, so the game's own layout group re-asserts the row. Both
///   are change-gated, both have generous thresholds (a legitimate layout y for a given pooled
///   entry is constant while the entry count is), and the y guard stands down while the entry set
///   differs from the latch or inside a settle window after a slide.</item>
/// <item><b>DRIFT IS NOW MEASURED IN 3 AXES.</b> The old integrity number only watched x, which is
///   why every log said "Foreign-writer drift: 0,00 px" while the row was being pushed off its axis
///   in y and z.</item>
/// </list>
///
/// SCOPE: only ever runs while <see cref="InitiativeTrackSurface"/> holds a converted panel, so
/// flat/[Dev] play is 100 % vanilla. MULTIPLAYER: every write lands on this client's own UI
/// transforms — no rules state, nothing on the wire, no game-side callback suppressed. The peers'
/// mirrors are per-frame transform copies (<c>RemoteWidgetMirror</c>), so they now show the slide
/// instead of the snap; <see cref="Net.RemoteInitiativeTrack"/> only ever writes CLONE nodes
/// (<c>_mirror.CloneOf(...)</c>, which returns null on a miss — verified) and cannot reach these
/// entries. Because the repair is driven from THIS client's own settled layout, each client fixes
/// its own row at its own tray orientation; nothing has to agree across the wire.
/// </summary>
internal sealed class InitiativeReorderSlide
{
    /// <summary>One portrait's journey along the row, in holder-local uGUI pixels.</summary>
    private struct Leg
    {
        public Transform Entry;
        public float FromX;
        public float ToX;
        public float RestY;
        public float RestZ;

        /// <summary>Local x this slide wrote last frame — the baseline the drift check compares to.</summary>
        public float LastX;
    }

    /// <summary>Floor on the slide time: a serialized 0 would make the animation unwatchable.</summary>
    private const float MinDuration = 0.2f;

    /// <summary>Ceiling: a nonsense serialized value must not freeze the row's layout for minutes.</summary>
    private const float MaxDuration = 4f;

    /// <summary>Below this (holder-local px) a "move" is layout noise — not worth animating.</summary>
    private const float TravelEpsilon = 0.5f;

    /// <summary>Hard stop: even a wedged track hands the layout group back after this long.</summary>
    private const float WatchdogSeconds = 8f;

    /// <summary>
    /// One row entry's SETTLED pose in holder-local uGUI pixels — sampled only while the game's own
    /// <c>HorizontalLayoutGroup</c> owns the row, which is the one state in which these numbers are
    /// the layout's truth rather than whatever a stray world-space write left behind.
    /// </summary>
    private struct Rest
    {
        public float X;
        public float Y;
        public float Z;
    }

    /// <summary>
    /// Out-of-plane tolerance (holder-local px) for the row's entry roots. NOTHING in uGUI rewrites
    /// a child's local z, and the row's direct children are authored flat (the authored depth lives
    /// on transforms NESTED inside each portrait — see <c>InitiativeTrackSurface.NormalizeDepth</c>),
    /// so any measurable z on an entry ROOT is leaked geometry and is written straight back.
    /// </summary>
    private const float PlaneRepairPixels = 2f;

    /// <summary>
    /// Off-axis tolerance (holder-local px) for a row entry's local y. Deliberately far above any
    /// legitimate layout nuance: for a given POOLED entry the layout group's cross-axis position is
    /// constant while the entry count is (players and enemies come from separate pools, so an entry
    /// object never changes height), while the leak measured on hardware was 84-120 px.
    /// </summary>
    private const float AxisRepairPixels = 16f;

    /// <summary>
    /// After a slide (or an abort) the layout group re-lays the row out on the next canvas update;
    /// the y guard stands down for this long so it can never argue with that fresh layout pass.
    /// The z guard keeps running — no layout pass writes z.
    /// </summary>
    private const float GuardSettleSeconds = 1f;

    /// <summary>At most one row-axis repair log per this many seconds (it is per-frame code).</summary>
    private const float GuardLogInterval = 5f;

    private readonly List<Leg> _legs = new(16);
    private readonly List<float> _slots = new(16);

    /// <summary>The row's settled pose per entry transform — see <see cref="LatchRest"/>.</summary>
    private readonly Dictionary<Transform, Rest> _rest = new(16);

    /// <summary>Active direct children of the holder this frame (reused; no per-frame allocation).</summary>
    private readonly List<Transform> _rowScratch = new(16);

    private HorizontalLayoutGroup? _layoutGroup;
    private ContentSizeFitter? _fitter;

    /// <summary>The row holder the current hold/repair is scoped to (for the forced layout rebuild).</summary>
    private RectTransform? _holderRect;

    private float _duration;
    private float _elapsed;
    private int _frames;

    /// <summary>Vanilla's <c>isAnimating|animationDelayed</c> as of last frame — for the rising edge.</summary>
    private bool _sawAnimating;

    /// <summary><c>moveXAnimations.Count</c> as of last frame — for the empty→non-empty edge.</summary>
    private int _sawBatchCount;

    /// <summary>A reorder was announced and this slide will take it over on the frame the tweens exist.</summary>
    private bool _armed;

    /// <summary><see cref="Time.unscaledTime"/> until which the y half of the row-axis guard stands down.</summary>
    private float _guardSettleUntil;

    /// <summary>Rate limit for the row-axis repair log.</summary>
    private float _nextGuardLog;

    /// <summary>How many world-x tweens were cancelled when the slide took over.</summary>
    private int _cancelled;

    /// <summary>Frames on which the game re-enabled a layout writer and we put it back.</summary>
    private int _suppressedLayout;
    private int _suppressedFitter;

    /// <summary>Elapsed slide time at which the game's own window closed (-1 = still open).</summary>
    private float _gameWindowClosedAt = -1f;

    /// <summary>
    /// Largest gap (holder-local px) ever found between where this slide left an entry last frame
    /// and where it was found this frame. Anything above a rounding epsilon means SOMEONE ELSE
    /// wrote a row entry mid-slide — the integrity number that settles a repeat report without
    /// another round of guessing.
    /// </summary>
    private float _maxDrift;

    /// <summary>True while a mod-driven slide is in flight.</summary>
    public bool Active => _legs.Count > 0;

    /// <summary>
    /// Per-frame from <see cref="InitiativeTrackSurface.LateTick"/> — i.e. after every Update-phase
    /// transform writer in the process (LeanTween's updater included) has had its say.
    /// </summary>
    public void Tick(InitiativeTrack? track)
    {
        if (track == null)
        {
            Abort("track gone");
            return;
        }
        Transform? holder = track.initiativeTrackHolder;
        if (holder == null)
        {
            Abort("the row holder is gone");
            return;
        }

        // ARM on the rising edge of vanilla's own animation flag. That edge is the coroutine's FIRST
        // segment (InitiativeTrack.cs:260-263): the layout writers are already off and NOT ONE tween
        // exists yet, so it is also the last moment at which the live transforms are guaranteed to
        // still be the settled layout — hence the latch here.
        bool animating = track.isAnimating || track.animationDelayed;
        if (animating && !_sawAnimating)
        {
            _armed = true;
            // Latch only when nothing is in flight: mid-slide positions are this class's own
            // interpolation, not a rest pose, and the latch from before the running slide is still
            // the row's last layout-owned truth.
            if (!Active)
                LatchRest(holder);
        }
        _sawAnimating = animating;

        // Second arming path: vanilla clears moveXAnimations at the end of every reorder
        // (InitiativeTrack.cs:287 / :329), so empty→non-empty is a once-per-reorder edge too. It
        // covers the reorder that starts while this slide is still running (vanilla's own
        // `animationDelayed` re-entry, where isAnimating never falls and there is no rising edge to
        // see) and the one driven from a callback that runs after our LateUpdate.
        List<LTDescr>? batch = track.moveXAnimations;
        int pending = batch != null ? batch.Count : 0;
        if (pending > 0 && _sawBatchCount == 0)
            _armed = true;
        _sawBatchCount = pending;

        // FIRE once the batch exists. Deliberately NOT keyed on batch[0].id: cancelling a tween sets
        // its pool counter to uint.MaxValue (LeanTween.cs:234) and LTDescr.id is
        // _id | (counter << 16) (LTDescr.cs:139), so our own cancel CHANGES the id we would be
        // keying on — which is what produced the duplicated "SLIDE start" in every hardware log.
        if (_armed && pending > 0)
        {
            _armed = false;
            Begin(track, holder);
            return; // first visible frame is the row at its REST pose — no jump
        }

        if (Active)
        {
            Advance(track);
            return;
        }

        // Idle: the layout group owns the row again. Repair anything a world-space write left off
        // the row's axis, then re-latch the settled truth for the next reorder.
        GuardRowAxis(holder, animating);
    }

    // ---------------------------------------------------------------- rest pose + row-axis guard --

    /// <summary>
    /// Sample the row's SETTLED pose: every active direct child of the holder, in holder-local
    /// uGUI pixels. Called only where the game's own layout group is the author of these numbers —
    /// on the <c>isAnimating</c> rising edge (layout writers just disabled, no tween created yet)
    /// and on idle frames after <see cref="GuardRowAxis"/> has had its say. Rebuilt wholesale so a
    /// destroyed or re-pooled entry can never leave a stale key behind.
    /// </summary>
    private void LatchRest(Transform holder)
    {
        _rest.Clear();
        for (int i = 0; i < holder.childCount; i++)
        {
            Transform child = holder.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;
            Vector3 lp = child.localPosition;
            _rest[child] = new Rest { X = lp.x, Y = lp.y, Z = lp.z };
        }
    }

    /// <summary>
    /// Hold the row on ONE AXIS while nothing is animating it.
    ///
    /// Two different authorities, so two different rules:
    /// <list type="bullet">
    /// <item><b>local z</b> — nobody owns it. <c>HorizontalLayoutGroup</c> writes x and y on every
    ///   rebuild and never touches z, so an out-of-plane offset leaked by a WORLD-space write is
    ///   permanent and ACCUMULATES across rounds until the portrait projects somewhere it cannot be
    ///   seen (the missing-portrait report). Written straight back to the latch, always.</item>
    /// <item><b>local y</b> — the game's layout group owns it. The latch IS that group's own last
    ///   settled output for this very transform, so restoring it is a restore and not an invention;
    ///   the holder is marked for rebuild in the same breath so the group re-asserts the row itself.
    ///   Held to a coarse threshold, stood down while the entry set differs from the latch (a
    ///   genuine re-layout is imminent) and inside the post-slide settle window.</item>
    /// </list>
    /// </summary>
    private void GuardRowAxis(Transform holder, bool animating)
    {
        // Collect the row as it stands, and decide whether the latch still describes THIS set.
        _rowScratch.Clear();
        bool setMatches = true;
        for (int i = 0; i < holder.childCount; i++)
        {
            Transform child = holder.GetChild(i);
            if (!child.gameObject.activeSelf)
                continue;
            _rowScratch.Add(child);
            if (!_rest.ContainsKey(child))
                setMatches = false;
        }
        if (_rowScratch.Count != _rest.Count)
            setMatches = false;

        if (_rest.Count == 0 || !setMatches)
        {
            // Nothing to compare against (first frames after a conversion, or the row just changed
            // shape). The layout group is in charge here; take its output as the new truth.
            if (!animating)
                LatchRest(holder);
            return;
        }

        bool guardY = !animating && Time.unscaledTime >= _guardSettleUntil;
        int planeFixed = 0;
        int axisFixed = 0;
        float worstZ = 0f;
        float worstY = 0f;

        for (int i = 0; i < _rowScratch.Count; i++)
        {
            Transform entry = _rowScratch[i];
            Rest rest = _rest[entry];
            Vector3 lp = entry.localPosition;

            float dz = lp.z - rest.Z;
            float dy = lp.y - rest.Y;
            bool fixZ = Mathf.Abs(dz) > PlaneRepairPixels;
            bool fixY = guardY && Mathf.Abs(dy) > AxisRepairPixels;
            if (!fixZ && !fixY)
                continue;

            if (fixZ)
            {
                planeFixed++;
                if (Mathf.Abs(dz) > Mathf.Abs(worstZ))
                    worstZ = dz;
            }
            if (fixY)
            {
                axisFixed++;
                if (Mathf.Abs(dy) > Mathf.Abs(worstY))
                    worstY = dy;
            }
            entry.localPosition = new Vector3(lp.x, fixY ? rest.Y : lp.y, fixZ ? rest.Z : lp.z);
        }

        if (planeFixed == 0 && axisFixed == 0)
        {
            // Clean row — this is the settled truth the next reorder will be planned from.
            if (!animating)
                LatchRest(holder);
            return;
        }

        if (axisFixed > 0)
            MarkRowForRebuild(holder);

        if (Time.unscaledTime < _nextGuardLog)
            return;
        _nextGuardLog = Time.unscaledTime + GuardLogInterval;
        VRLog.Info("WorldUI",
            $"INITIATIVE ROW AXIS GUARD: put {planeFixed} entr(y/ies) back INTO the panel plane " +
            $"(worst local z {worstZ:F1} px off) and {axisFixed} back ONTO the row axis (worst local " +
            $"y {worstY:F1} px off) out of {_rowScratch.Count}. A world-space write moved them: " +
            "vanilla slides/snaps transform.position.X (LeanTween moveX, and StopUpdateActors' " +
            "outright assignment), which is only 'along the row' on the flat screen-space canvas — " +
            "on the docked, tray-mounted panel the same delta also has an off-axis y and an " +
            "out-of-plane z component, and uGUI's layout group rewrites x/y but NEVER z, so the z " +
            "part used to accumulate until a portrait projected out of view. Restored from the " +
            "row's own last SETTLED layout; the holder was marked for rebuild so the game's " +
            "HorizontalLayoutGroup re-asserts the row itself.");
    }

    /// <summary>Ask the game's own layout group to re-lay the row out. Belt and braces next to
    /// re-enabling it: <c>enabled = true</c> only marks the layout dirty when the flag actually
    /// CHANGED, so a component that was already on would otherwise leave a stale row standing.</summary>
    private static void MarkRowForRebuild(Transform? holder)
    {
        if (holder is RectTransform rect)
            LayoutRebuilder.MarkLayoutForRebuild(rect);
    }

    /// <summary>
    /// Capture the slide plan in the ROW's own space and take the world-x tweens off the board.
    ///
    /// DESTINATION ASSIGNMENT, re-derived rather than read back: vanilla hands the i-th entry of
    /// the freshly sorted <c>actorsUI</c> the (n-1-i)-th recorded slot, and the recorded slots are
    /// the holder's children in SIBLING order — which a <c>HorizontalLayoutGroup</c> lays out
    /// left-to-right, i.e. in ascending local x (descending when <c>reverseArrangement</c> is set).
    /// The slot positions themselves are untouched at this point (the layout group is disabled, so
    /// vanilla's <c>SetAsFirstSibling</c> re-sort moved nothing), so sorting the entries' REST
    /// local x reproduces the same multiset the game recorded — without depending on the world
    /// axis the game happened to record it along, which is the whole point.
    ///
    /// EVERY NUMBER COMES FROM <see cref="_rest"/>, NOT FROM THE LIVE TRANSFORM (round 2). By the
    /// time this runs, at least one LeanTween world-x step has already landed (the tweens are
    /// created at <c>WaitForEndOfFrame</c> of the previous frame and integrated from an Update, both
    /// ahead of our LateUpdate) — reading the live pose would adopt that leak as the row's rest
    /// pose and hold it for the whole slide, which is precisely the Y staircase the peer reported.
    /// The latch is the layout group's own last settled output, so building the plan from it makes
    /// the slide's very first frame a REPAIR.
    /// </summary>
    private void Begin(InitiativeTrack track, Transform holder)
    {
        RestoreLayoutWriters(); // a slide already running is superseded by this one
        _legs.Clear();
        _slots.Clear();
        _layoutGroup = null;
        _fitter = null;
        _holderRect = holder as RectTransform;
        _elapsed = 0f;
        _frames = 0;
        _cancelled = 0;
        _suppressedLayout = 0;
        _suppressedFitter = 0;
        _gameWindowClosedAt = -1f;
        _maxDrift = 0f;

        // CANCEL FIRST — ahead of every bail below. Cancel-without-onComplete is exactly what
        // vanilla's own StopUpdateActors does with these handles (InitiativeTrack.cs:325-329), so
        // the list is left in a state the game already knows how to clean up. There is no bail path
        // on which leaving a world-x tween alive is correct: on the docked panel its travel is
        // foreshortened AND it pushes the portrait off the row axis and out of the panel plane.
        CancelWorldTweens(track);

        List<InitiativeTrackActorBehaviour>? actors = track.actorsUI;
        if (actors == null || actors.Count == 0)
        {
            Skipped("the track has no entries", holder);
            return;
        }

        // Active entries in the game's own (already re-sorted) order, at their REST pose.
        for (int i = 0; i < actors.Count; i++)
        {
            InitiativeTrackActorBehaviour beh = actors[i];
            if (beh == null || !beh.gameObject.activeSelf || beh.transform.parent != holder)
                continue;
            Transform entry = beh.transform;
            if (!_rest.TryGetValue(entry, out Rest rest))
            {
                // No latch for this entry (pooled in during the reorder). The live pose is the only
                // thing there is, and it is also what the layout group last wrote for it.
                Vector3 live = entry.localPosition;
                rest = new Rest { X = live.x, Y = live.y, Z = live.z };
                _rest[entry] = rest;
            }
            _legs.Add(new Leg
            {
                Entry = entry, FromX = rest.X, LastX = rest.X, RestY = rest.Y, RestZ = rest.Z,
            });
            _slots.Add(rest.X);
        }
        if (_legs.Count < 2)
        {
            _legs.Clear(); // a single entry has nowhere to go
            Skipped($"only {_slots.Count} active entr(y/ies) — nothing can move", holder);
            return;
        }

        _slots.Sort(); // ascending local x == the row left-to-right
        bool reversed = false;
        HorizontalLayoutGroup? group = track.layoutGroup;
        if (group != null)
            reversed = group.reverseArrangement;

        int n = _legs.Count;
        float maxTravel = 0f;
        float totalTravel = 0f;
        for (int i = 0; i < n; i++)
        {
            Leg leg = _legs[i];
            leg.ToX = reversed ? _slots[i] : _slots[n - 1 - i];
            _legs[i] = leg;
            float travel = Mathf.Abs(leg.ToX - leg.FromX);
            totalTravel += travel;
            if (travel > maxTravel)
                maxTravel = travel;
        }

        if (maxTravel < TravelEpsilon)
        {
            _legs.Clear(); // the sort was a no-op (same order as last round) — nothing to watch
            Skipped("the re-sort left every portrait in its own slot", holder);
            return;
        }

        _duration = Mathf.Clamp(track.trackReorderDuration, MinDuration, MaxDuration);
        _layoutGroup = track.layoutGroup;
        _fitter = track.contentSizeFitter;
        HoldLayoutWriters(count: false);

        float unityScale = Time.timeScale;
        float chronosScale = ChronosScale();
        VRLog.Info("WorldUI",
            $"INITIATIVE REORDER SLIDE start: {n} portrait(s) re-order, travel max {maxTravel:F0} px / " +
            $"total {totalTravel:F0} px along the ROW's local x, over {_duration:F2} s " +
            $"(game trackReorderDuration {track.trackReorderDuration:F2} s, easeOutQuad) on UNSCALED time. " +
            $"Cancelled {_cancelled} world-x LeanTween(s): vanilla slides transform.position.x, which is " +
            "only 'along the row' on the flat screen-space canvas — on the docked world-space panel it " +
            "foreshortens the travel and pushes the portraits out of the panel plane. " +
            $"Clocks at start: Time.timeScale={unityScale:F2} (LeanTween's) vs Chronos global " +
            $"{(chronosScale >= 0f ? chronosScale.ToString("F2") : "n/a")} (the coroutine window's) — " +
            "any divergence is what closes vanilla's window off-beat; the slide below ignores both. " +
            "Layout group + content-size fitter held OFF until it finishes, content re-fit and depth " +
            $"normalization held by the surface. Planned from the row's LATCHED rest pose ({_rest.Count} " +
            "entr(y/ies) sampled while the layout group last owned the row), never from the live " +
            "transforms — at least one vanilla world-x step always lands before this LateUpdate can " +
            "cancel, and the first slide frame REPAIRS it instead of adopting it.");
    }

    /// <summary>
    /// Take the game's world-x tweens off the board. Called BEFORE every bail in
    /// <see cref="Begin"/>: on the docked panel a world-x tween is never right, so no path may
    /// leave one running. Cancelling mutates <c>LTDescr.id</c> (the pool counter goes to
    /// <c>uint.MaxValue</c>), which is exactly why the trigger above is no longer keyed on it.
    /// </summary>
    private void CancelWorldTweens(InitiativeTrack track)
    {
        List<LTDescr>? batch = track.moveXAnimations;
        if (batch == null)
            return;
        for (int i = 0; i < batch.Count; i++)
        {
            LTDescr descr = batch[i];
            if (descr == null)
                continue;
            try
            {
                LeanTween.cancel(descr.id);
                _cancelled++;
            }
            catch { /* a tween the engine already retired — nothing to cancel */ }
        }
    }

    /// <summary>
    /// This reorder needs no slide (no entries, one entry, or a re-sort that moved nobody). Say so:
    /// the peer's hardware log had reorders with a "start" and neither a "done" nor an "aborted"
    /// because this class used to bail in silence, which cost a round of guessing. The world-x
    /// tweens are already cancelled by the time we get here, so the row is handed to the game's own
    /// layout — nudged, because a rebuild is the only thing that puts a leaked offset right.
    /// </summary>
    private void Skipped(string why, Transform holder)
    {
        RestoreLayoutWriters();
        MarkRowForRebuild(holder);
        _guardSettleUntil = Time.unscaledTime + GuardSettleSeconds;
        VRLog.Info("WorldUI",
            $"INITIATIVE REORDER SLIDE skipped ({why}) — {_cancelled} vanilla world-x LeanTween(s) " +
            "cancelled anyway (on the docked panel they would slide the portraits off the row axis " +
            "and out of the panel plane, and nothing in uGUI ever rewrites a child's local z), and " +
            "the row handed straight back to the game's HorizontalLayoutGroup with a rebuild request.");
    }

    /// <summary>Advance one frame; snap + release everything on the last one.</summary>
    private void Advance(InitiativeTrack track)
    {
        // The game's own window: once isAnimating drops, vanilla has already re-enabled its layout
        // writers and (without this slide) the row would have snapped. Recorded, not obeyed.
        if (_gameWindowClosedAt < 0f && !track.isAnimating)
            _gameWindowClosedAt = _elapsed;

        _elapsed += Time.unscaledDeltaTime;
        _frames++;
        bool done = _elapsed >= _duration || _elapsed >= WatchdogSeconds;
        float t = _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 1f;
        float e = done ? 1f : 1f - (1f - t) * (1f - t); // easeOutQuad, the shape vanilla asked for

        HoldLayoutWriters(count: true);

        for (int i = 0; i < _legs.Count; i++)
        {
            Leg leg = _legs[i];
            if (leg.Entry == null)
            {
                Abort("an entry was destroyed mid-slide"); // pooled row rebuilt under us
                return;
            }
            // Integrity: nobody but this slide should have touched the entry since last frame.
            // Measured on ALL THREE axes (round 2) — the x-only check reported a clean 0,00 px in
            // every hardware log while vanilla's world-x writes were pushing the row off its axis in
            // y and out of the panel plane in z, which is the whole defect.
            Vector3 live = leg.Entry.localPosition;
            float drift = Mathf.Abs(live.x - leg.LastX);
            drift = Mathf.Max(drift, Mathf.Abs(live.y - leg.RestY));
            drift = Mathf.Max(drift, Mathf.Abs(live.z - leg.RestZ));
            if (drift > _maxDrift)
                _maxDrift = drift;

            float x = Mathf.Lerp(leg.FromX, leg.ToX, e);
            leg.Entry.localPosition = new Vector3(x, leg.RestY, leg.RestZ);
            leg.LastX = x;
            _legs[i] = leg;
        }

        if (!done)
            return;

        int entries = _legs.Count;
        float elapsed = _elapsed;
        float closedAt = _gameWindowClosedAt;
        int suppressedLayout = _suppressedLayout;
        int suppressedFitter = _suppressedFitter;
        int frames = _frames;
        float drifted = _maxDrift;

        _legs.Clear();
        RestoreLayoutWriters();
        MarkRowForRebuild(_holderRect);
        _guardSettleUntil = Time.unscaledTime + GuardSettleSeconds;

        string window = closedAt < 0f
            ? "the game's own window was still open when the slide ended (both clocks agreed this round)"
            : $"the game's own window closed after {closedAt:F2} s = {(_duration > 0f ? closedAt / _duration * 100f : 100f):F0} % " +
              "of the slide — vanilla would have SNAPPED the row here; the slide outlived it and played to the end";

        VRLog.Info("WorldUI",
            $"INITIATIVE REORDER SLIDE done: {entries} portrait(s) reached their sorted slot after " +
            $"{elapsed:F2} s / {frames} frame(s) (planned {_duration:F2} s) — the FULL travel was on " +
            $"screen. {window}. Writers suppressed while it ran: layout group re-enable " +
            $"x{suppressedLayout}, content-size fitter re-enable x{suppressedFitter} (0/0 = the game " +
            "never tried); the surface held the content re-fit and the depth normalization for the " +
            $"same span. Foreign-writer drift (x|y|z, round 2 — was x-only): {drifted:F2} px. It is " +
            "EXPECTED to be non-zero on the FIRST frame and only there: that is the vanilla world-x " +
            "step that always lands before this LateUpdate can cancel (tweens are created at " +
            "WaitForEndOfFrame and integrated from an Update), and the slide overwrites it from the " +
            "latched rest pose instead of adopting it. A figure that keeps growing after frame 1 " +
            "means something ELSE still moves a row entry mid-slide. Both layout components are back " +
            "ON and the holder was marked for rebuild — the row is under the game's " +
            "HorizontalLayoutGroup again.");
    }

    /// <summary>
    /// Keep vanilla's two layout writers off. They are the ONLY things that can move a row entry
    /// while the slide runs — <c>HorizontalLayoutGroup</c> drives every child's anchored position on
    /// the next rebuild, and <c>ContentSizeFitter</c> resizes the holder under it. Counting the
    /// re-enables is what turns "the animation looked cut off" into a number in the log.
    /// </summary>
    private void HoldLayoutWriters(bool count)
    {
        if (_layoutGroup != null && _layoutGroup.enabled)
        {
            _layoutGroup.enabled = false;
            if (count)
                _suppressedLayout++;
        }
        if (_fitter != null && _fitter.enabled)
        {
            _fitter.enabled = false;
            if (count)
                _suppressedFitter++;
        }
    }

    /// <summary>Hand the row back to the game's layout — the state vanilla's coroutine ends in.</summary>
    private void RestoreLayoutWriters()
    {
        if (_layoutGroup != null)
            _layoutGroup.enabled = true;
        if (_fitter != null)
            _fitter.enabled = true;
        _layoutGroup = null;
        _fitter = null;
    }

    /// <summary>
    /// Drop a slide in flight (panel released, surface shutdown, row rebuilt) leaving the row where
    /// the game expects it: layout writers ON, so the next rebuild lays the sorted order out — and,
    /// for every leg we were still driving, its axis-normal REST pose written back first. The layout
    /// group will redo x and y, but nothing in uGUI ever rewrites a child's local z, so an abort that
    /// left the leaked out-of-plane offset standing would be a permanent one.
    /// </summary>
    public void Abort(string reason)
    {
        _armed = false;
        _sawAnimating = false;
        _sawBatchCount = 0;

        if (_legs.Count == 0 && _layoutGroup == null && _fitter == null)
        {
            _rest.Clear(); // the next conversion latches the row's rest pose from scratch
            return;
        }
        int entries = _legs.Count;
        float elapsed = _elapsed;
        int repaired = 0;
        for (int i = 0; i < _legs.Count; i++)
        {
            Leg leg = _legs[i];
            if (leg.Entry == null)
                continue;
            Vector3 lp = leg.Entry.localPosition;
            if (Mathf.Approximately(lp.y, leg.RestY) && Mathf.Approximately(lp.z, leg.RestZ))
                continue;
            leg.Entry.localPosition = new Vector3(lp.x, leg.RestY, leg.RestZ);
            repaired++;
        }
        _legs.Clear();
        RestoreLayoutWriters();
        MarkRowForRebuild(_holderRect);
        _holderRect = null;
        _rest.Clear();
        _guardSettleUntil = Time.unscaledTime + GuardSettleSeconds;
        if (entries > 0)
        {
            VRLog.Info("WorldUI",
                $"INITIATIVE REORDER SLIDE aborted after {elapsed:F2} s of {_duration:F2} s ({reason}) — " +
                $"{entries} portrait(s) released to the game's layout group, which lays the sorted order " +
                $"out on the next rebuild (requested here). {repaired} of them were put back onto the " +
                "row axis / into the panel plane from the latched rest pose first; the layout group " +
                "redoes x and y, but NOTHING in uGUI rewrites a child's local z. No transform is left held.");
        }
    }

    /// <summary>
    /// The Chronos global clock scale the game's own animation window is measured in — the other
    /// half of the clock seam documented on this class. Read defensively: Chronos is a game
    /// singleton and a torn-down scene may not have one.
    /// </summary>
    private static float ChronosScale()
    {
        try
        {
            Chronos.Timekeeper keeper = Chronos.Timekeeper.instance;
            Chronos.GlobalClock? clock = keeper != null ? keeper.m_GlobalClock : null;
            return clock != null ? clock.timeScale : -1f;
        }
        catch
        {
            return -1f;
        }
    }
}
