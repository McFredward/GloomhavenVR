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
/// SCOPE: only ever runs while <see cref="InitiativeTrackSurface"/> holds a converted panel, so
/// flat/[Dev] play is 100 % vanilla. MULTIPLAYER: every write lands on this client's own UI
/// transforms — no rules state, nothing on the wire, no game-side callback suppressed. The peers'
/// mirrors are per-frame transform copies (<c>RemoteWidgetMirror</c>), so they now show the slide
/// instead of the snap; <see cref="Net.RemoteInitiativeTrack"/> only ever writes CLONE nodes
/// (<c>_mirror.CloneOf(...)</c>) and cannot reach these entries.
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

    private readonly List<Leg> _legs = new(16);
    private readonly List<float> _slots = new(16);

    private HorizontalLayoutGroup? _layoutGroup;
    private ContentSizeFitter? _fitter;

    private float _duration;
    private float _elapsed;
    private int _frames;

    /// <summary>Unique id of the LeanTween batch this slide was built from (0 = none seen yet).</summary>
    private int _batchId;

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

        // A NEW batch of world-x tweens is the game telling us a reorder just started. The batch's
        // first LTDescr id is the generation key: it stays put for the whole reorder (we cancel the
        // tweens, so the game's onComplete never prunes the list — vanilla clears it wholesale at
        // the end of the coroutine) and a later reorder always brings a different id.
        List<LTDescr>? batch = track.moveXAnimations;
        int id = batch != null && batch.Count > 0 && batch[0] != null ? batch[0].id : 0;
        if (id != 0 && id != _batchId)
        {
            _batchId = id;
            Begin(track);
            return; // first visible frame is the row exactly where it stands — no jump
        }

        if (Active)
            Advance(track);
    }

    /// <summary>
    /// Capture the slide plan in the ROW's own space and take the world-x tweens off the board.
    ///
    /// DESTINATION ASSIGNMENT, re-derived rather than read back: vanilla hands the i-th entry of
    /// the freshly sorted <c>actorsUI</c> the (n-1-i)-th recorded slot, and the recorded slots are
    /// the holder's children in SIBLING order — which a <c>HorizontalLayoutGroup</c> lays out
    /// left-to-right, i.e. in ascending local x (descending when <c>reverseArrangement</c> is set).
    /// The slot positions themselves are untouched at this point (the layout group is disabled, so
    /// vanilla's <c>SetAsFirstSibling</c> re-sort moved nothing), so sorting the entries' CURRENT
    /// local x reproduces the same multiset the game recorded — without depending on the world
    /// axis the game happened to record it along, which is the whole point.
    /// </summary>
    private void Begin(InitiativeTrack track)
    {
        RestoreLayoutWriters(); // a slide already running is superseded by this one
        _legs.Clear();
        _slots.Clear();
        _layoutGroup = null;
        _fitter = null;
        _elapsed = 0f;
        _frames = 0;
        _cancelled = 0;
        _suppressedLayout = 0;
        _suppressedFitter = 0;
        _gameWindowClosedAt = -1f;
        _maxDrift = 0f;

        Transform? holder = track.initiativeTrackHolder;
        List<InitiativeTrackActorBehaviour>? actors = track.actorsUI;
        if (holder == null || actors == null || actors.Count == 0)
            return;

        // Active entries in the game's own (already re-sorted) order.
        for (int i = 0; i < actors.Count; i++)
        {
            InitiativeTrackActorBehaviour beh = actors[i];
            if (beh == null || !beh.gameObject.activeSelf || beh.transform.parent != holder)
                continue;
            Vector3 lp = beh.transform.localPosition;
            _legs.Add(new Leg
            {
                Entry = beh.transform, FromX = lp.x, LastX = lp.x, RestY = lp.y, RestZ = lp.z,
            });
            _slots.Add(lp.x);
        }
        if (_legs.Count < 2)
        {
            _legs.Clear(); // a single entry has nowhere to go
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
            return;
        }

        // Take the game's world-x tweens off the board. Cancel-without-onComplete is exactly what
        // vanilla's own StopUpdateActors does with these handles (InitiativeTrack.cs:325-329), so
        // the list is left in a state the game already knows how to clean up.
        List<LTDescr>? batch = track.moveXAnimations;
        if (batch != null)
        {
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
            "normalization held by the surface.");
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
            float drift = Mathf.Abs(leg.Entry.localPosition.x - leg.LastX);
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
            $"same span. Foreign-writer drift: {drifted:F2} px (anything above ~0.5 px means something " +
            "ELSE still moves a row entry mid-slide — that, not the clock, would be the next lead). " +
            "Both layout components are back ON — the row is under the game's HorizontalLayoutGroup again.");
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
    /// the game expects it: layout writers ON, so the next rebuild lays the sorted order out.
    /// </summary>
    public void Abort(string reason)
    {
        if (_legs.Count == 0 && _layoutGroup == null && _fitter == null)
        {
            _batchId = 0; // next conversion starts a fresh generation key
            return;
        }
        int entries = _legs.Count;
        float elapsed = _elapsed;
        _legs.Clear();
        RestoreLayoutWriters();
        _batchId = 0;
        if (entries > 0)
        {
            VRLog.Info("WorldUI",
                $"INITIATIVE REORDER SLIDE aborted after {elapsed:F2} s of {_duration:F2} s ({reason}) — " +
                $"{entries} portrait(s) released to the game's layout group, which lays the sorted order " +
                "out on the next rebuild. No transform is left held.");
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
