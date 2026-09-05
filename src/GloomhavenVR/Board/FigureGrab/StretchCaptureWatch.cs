using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// THE CAPTURE STATE OF THE TWO-HAND RESIZE GESTURE, MADE READABLE — and the census of what that
/// state switched off while it held.
///
/// <para><b>WHY THIS EXISTS (2026-09-03).</b> User, verbatim: <i>"Ist in einer Hand eine Figur,
/// dann reagieren bei den props weder das highlighting noch kann ich die props grabben."</i> The
/// named suspect is <see cref="FigureStretch"/>'s CAPTURE state: <c>FigureStretch.Engaged</c> is
/// <c>Captured || Active</c>, <b>Captured is set with no trigger
/// at all</b> — purely by the free hand entering a shell around the OTHER hand's held object —
/// and <c>GrabbableProp.AllowsHand</c> answers <c>false</c> for the whole frame while it holds.
/// For a prop that method IS the election, so a <c>false</c> there removes the prop from the
/// <c>ProximityGrabber</c>'s candidate list and takes the hover highlight with it. That is exactly
/// "neither highlighting nor grabbing".</para>
///
/// <para><b>AND WHY IT HAD TO BE BUILT BEFORE THE REMEDY.</b> The ModBuild 396 hardware log
/// records only the TRIGGER-level engagement (<c>"… STRETCH engaged on …"</c>, 13 lines). The
/// <c>Captured</c> half — the state that actually vetoes props — was <b>never logged at all</b>,
/// so the code path could be named and its frequency could not be measured. The one thing that
/// log DOES settle is that the shell is enormous: the gesture engaged on <c>SavvasIcestormID</c>
/// at 470–523 mm from the mini's CENTRE while the capture test (which measures to the SURFACE)
/// was inside its 80 mm threshold, and at L28523 a prop hover ended with the log's own words
/// <c>"it is 29 mm real from the palm, inside the 176 mm exit ring, so DISTANCE is not what ended
/// this hover"</c>. A hand 29 mm from a prop lost that prop to a shell drawn around a miniature in
/// the other hand.</para>
///
/// <para><b>WHAT THIS PRINTS, and the question each line settles.</b></para>
/// <list type="number">
///   <item><b>CAPTURE ON</b> — one line per episode: which hand, which held object, the SURFACE
///   distance against the threshold, and the radius and NAME of the widest renderer the shell was
///   drawn around, against the sanity ceiling that admitted it. That last field is the one that
///   decides whether the shell is the player's hand being at the mini or a stray effect mesh
///   inflating the AABB — "name the blocker, not the number".</item>
///   <item><b>CAPTURE OFF</b> — how many frames it held, whether a gesture ever started inside it,
///   and the NAMES of the props it vetoed while it held. A capture that ends having vetoed props
///   and started no gesture is the defect, measured.</item>
///   <item><b>CAPTURE REFUSED (pick wins)</b> — the new discriminator firing: the hand was inside
///   a prop's own pick volume, so the shell stood down. Its presence is the remedy running; its
///   absence while the user reports the symptom means the remedy never ran.</item>
///   <item><b>The census</b>, on a fixed cadence, which is where the ZEROES live.</item>
/// </list>
///
/// <para><b>THE ZERO IS THE POINT.</b> <see cref="EmitCensus"/> prints on its cadence whether or
/// not anything happened, and its text separates the three states a reader has to tell apart: the
/// gesture tick never ran (no line at all — the census is emitted from
/// <c>FigureStretch.Tick</c>, so its ABSENCE means the driver is not ticking); the tick ran and no
/// hand ever had a stretch target in the other hand (<c>with a target 0</c>); and the tick ran,
/// targets existed and no hand was ever captured (<c>captured 0</c>). "No hand was captured this
/// session" is a printed sentence here, never an absent line.</para>
///
/// <para><b>Cost.</b> Two counter increments per hand per frame and nothing else in the steady
/// state; the strings are composed only on an edge or on the 30 s cadence. Nothing here is read by
/// any mechanism — see <see cref="FigureStretch"/> for the one register that IS
/// load-bearing, which deliberately lives in the gesture rather than in this file.</para>
///
/// <para><b>Multiplayer.</b> Local presentation telemetry. It observes one player's hands, writes
/// no game state and no wire field, and its verdicts gate only that player's own local grab.</para>
/// </summary>
internal static class StretchCaptureWatch
{
    /// <summary>Referenced by the class doc so the sentence about a trigger-free capture points at
    /// something a reader can jump to. <see cref="FigureStretch"/>'s <c>HandState.Captured</c> is
    /// private, and this project does not widen a field's visibility for a doc link.</summary>
    private const string HandState_Captured_Note = "FigureStretch.HandState.Captured";

    /// <summary>Census cadence, unscaled seconds. Two lines a minute for the whole session is the
    /// price of a readable zero; the first one is emitted early (see <see cref="FirstCensusSeconds"/>)
    /// so a short round still carries one.</summary>
    private const float CensusSeconds = 30f;

    /// <summary>Delay before the FIRST census, unscaled seconds — short, so a five-minute hardware
    /// round is not decided by a cadence that had not come round yet.</summary>
    private const float FirstCensusSeconds = 10f;

    /// <summary>Capture episodes that print their ON/OFF pair in full. Past this the census still
    /// counts every one of them; what stops is the per-episode narration.</summary>
    private const int EpisodeBudget = 20;

    /// <summary>Refusal edges ("the pick beat the shell") that print in full, same argument.</summary>
    private const int RefusalBudget = 16;

    /// <summary>Vetoed prop names remembered per episode. A hand sweeping a crowded board can pass
    /// many; the names are what identify the defect and four of them say as much as forty.</summary>
    private const int VetoNameBudget = 6;

    private sealed class HandWatch
    {
        // ---- live episode state -------------------------------------------------------------
        public bool Captured;
        public int Frames;
        public bool GestureStarted;
        public string TargetLabel = string.Empty;
        public float SurfaceMm;
        public float ThresholdMm;
        public float BodyRadiusMm;
        public string BodyRenderer = string.Empty;
        public float CeilingMm;
        public float TotalRatio;
        public bool CentreFallback;
        public readonly HashSet<string> VetoedNames = new();
        public int VetoedProps;

        // ---- census totals, reset every cadence ----------------------------------------------
        public int Ticks;
        public int TicksWithTarget;
        public int Episodes;
        public int EpisodeFrames;
        public int Refusals;
        public int Vetoes;
        public int GesturesStarted;
        public string LastRefusalProp = string.Empty;
        public float LastRefusalMm;
        public float LastRefusalShellMm;

        /// <summary>ModBuild 445: a prop WAS in reach and the capture went ahead anyway, because
        /// the shell was the nearer of the two surfaces. The other side of the floor, counted so
        /// the census can say the floor is doing something rather than merely existing.</summary>
        public int Overruled;
        public string LastOverruledProp = string.Empty;
        public float LastOverruledMm;
        public float LastOverruledShellMm;
    }

    private static readonly HandWatch[] Hands = { new HandWatch(), new HandWatch() };

    private static int _episodeLogsLeft = EpisodeBudget;
    private static int _refusalLogsLeft = RefusalBudget;
    private static float _nextCensus;
    private static bool _censusArmed;

    /// <summary>Session totals — the census resets its per-window counters, so these are what a
    /// "did this EVER happen" question reads.</summary>
    private static int _sessionEpisodes;
    private static int _sessionRefusals;
    private static int _sessionVetoes;
    private static int _sessionOverruled;

    /// <summary>Drop everything (driver teardown, config-gate release). The BUDGETS are not reset:
    /// they are a per-session flood guard, not per-scenario state.</summary>
    internal static void Clear()
    {
        for (int i = 0; i < Hands.Length; i++)
        {
            HandWatch w = Hands[i];
            w.Captured = false;
            w.Frames = 0;
            w.GestureStarted = false;
            w.VetoedNames.Clear();
            w.VetoedProps = 0;
        }
    }

    /// <summary>One gesture tick was evaluated for this hand. <paramref name="hasTarget"/> is
    /// "the OTHER hand is holding something this gesture could resize" — the term that separates
    /// "the rule ran and found nothing" from "there was nothing to find".</summary>
    internal static void NoteTick(HandSide side, bool hasTarget)
    {
        HandWatch w = Hands[(int)side];
        w.Ticks++;
        if (hasTarget)
            w.TicksWithTarget++;
        if (!_censusArmed)
        {
            _censusArmed = true;
            _nextCensus = Time.unscaledTime + FirstCensusSeconds;
        }
    }

    /// <summary>
    /// The hand is inside the capture shell this frame. Called EVERY captured frame; the narration
    /// is edge-triggered inside.
    /// </summary>
    internal static void NoteCaptured(HandSide side, string targetLabel, float surfaceReal,
                                      float thresholdReal, float bodyRadiusReal,
                                      string bodyRenderer, float ceilingReal, float totalRatio,
                                      bool centreFallback)
    {
        HandWatch w = Hands[(int)side];
        w.SurfaceMm = surfaceReal * 1000f;
        w.ThresholdMm = thresholdReal * 1000f;
        w.BodyRadiusMm = float.IsPositiveInfinity(bodyRadiusReal) ? -1f : bodyRadiusReal * 1000f;
        w.BodyRenderer = bodyRenderer;
        w.CeilingMm = ceilingReal * 1000f;
        w.TotalRatio = totalRatio;
        w.CentreFallback = centreFallback;
        w.TargetLabel = targetLabel;
        w.Frames++;
        if (w.Captured)
            return;

        w.Captured = true;
        w.Episodes++;
        _sessionEpisodes++;
        w.Frames = 1;
        w.GestureStarted = false;
        w.VetoedNames.Clear();
        w.VetoedProps = 0;
        EmitCaptureOn(side, w);
    }

    /// <summary>The gesture actually started inside the current capture episode — recorded so the
    /// OFF line can say whether the shell was ever USED for what it exists for.</summary>
    internal static void NoteGestureStarted(HandSide side)
    {
        HandWatch w = Hands[(int)side];
        if (w.GestureStarted)
            return;
        w.GestureStarted = true;
        w.GesturesStarted++;
    }

    /// <summary>The hand is no longer captured. Idempotent — called on every un-captured tick.</summary>
    internal static void NoteReleased(HandSide side)
    {
        HandWatch w = Hands[(int)side];
        if (!w.Captured)
            return;
        w.Captured = false;
        w.EpisodeFrames += w.Frames;
        EmitCaptureOff(side, w);
        w.Frames = 0;
        w.VetoedNames.Clear();
        w.VetoedProps = 0;
    }

    /// <summary>
    /// THE DISCRIMINATOR FIRED: the hand was inside the capture shell AND inside a grabbable
    /// prop's own pick volume, and the prop won. This is the line that says the ModBuild 400
    /// remedy RAN — its absence while the user still reports the symptom means it did not.
    /// </summary>
    internal static void NoteCaptureRefused(HandSide side, string targetLabel, float surfaceReal,
                                            string rivalLabel, float rivalReal, float pickReal)
    {
        HandWatch w = Hands[(int)side];
        w.Refusals++;
        _sessionRefusals++;
        w.LastRefusalProp = rivalLabel;
        w.LastRefusalMm = rivalReal * 1000f;
        w.LastRefusalShellMm = surfaceReal * 1000f;
        if (_refusalLogsLeft <= 0)
            return;
        _refusalLogsLeft--;
        // HW-VERIFY: this is the line that proves the 2026-09-03 remedy for "props react to
        // nothing while a figure is held" actually executed. It must stay at a tier the DEFAULT
        // log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab",
            $"STRETCH CAPTURE REFUSED — {side} hand: the pinch point is {rivalReal * 1000f:F0} mm real "
            + $"inside {rivalLabel}'s own {pickReal * 1000f:F0} mm pick volume, while the other hand's "
            + $"{targetLabel} capture shell reads {surfaceReal * 1000f:F0} mm. THE PICK BEATS THE SHELL "
            + $"BECAUSE THE PROP IS THE NEARER SURFACE — {rivalReal * 1000f:F0} mm < "
            + $"{surfaceReal * 1000f:F0} mm, by {(surfaceReal - rivalReal) * 1000f:F0} mm — so the "
            + "hand is physically AT A PROP, the resize capture stands down, and the prop keeps its "
            + "highlight and its grab. THAT COMPARISON IS NEW IN ModBuild 445 AND IT IS THE POINT: "
            + "until this build the veto fired on the mere EXISTENCE of a prop in reach, so one prop "
            + "reporting a wrong distance disabled the whole gesture. On 2026-09-05 exactly that "
            + "happened — an enemy-drop gold pile registered a switched-off collider, whose "
            + "ClosestPoint hands back the query point, so it reported 0 mm from every hand position "
            + "on the board and this line printed ~800 times per machine with 'gestures started 0' "
            + "beside it. If you are reading a fresh log and the two numbers above are CLOSE, this "
            + "refusal is real; if the prop reads exactly 0 mm again while the shell number moves, "
            + "the prop's pick shape is dead and the [Props] DEAD PICK SHAPE line will name it. "
            + "The resize gesture is unchanged everywhere else and an already-running gesture is "
            + "never interrupted by this — to start one over a prop, move the free hand out of that "
            + "prop's pick volume ([FigureGrab] PickRadiusMillimeters) or move the held object away "
            + $"from it. ({_refusalLogsLeft} more refusal lines this session; the census below keeps "
            + "counting them after that.)");
    }

    /// <summary>
    /// THE FLOOR HELD: a grabbable prop was in reach of this hand and the capture went ahead
    /// anyway, because the other hand's held object is the NEARER surface.
    ///
    /// <para>Counted rather than logged per occurrence — this is the healthy case and it happens
    /// on every frame a player resizes a mini over a crowded board. What it buys is the census
    /// column: a build where the floor is doing nothing at all reads <c>overruled 0</c>, and a
    /// build where a prop is again reporting a distance it should not reads a large overruled
    /// count with the prop named. Both are answers; before ModBuild 445 there was no way to ask.
    /// </para>
    /// </summary>
    internal static void NoteVetoOverruled(HandSide side, string targetLabel, float surfaceReal,
                                           string rivalLabel, float rivalReal)
    {
        HandWatch w = Hands[(int)side];
        w.Overruled++;
        _sessionOverruled++;
        w.LastOverruledProp = rivalLabel;
        w.LastOverruledMm = rivalReal * 1000f;
        w.LastOverruledShellMm = surfaceReal * 1000f;
    }

    /// <summary>A prop answered <c>AllowsHand=false</c> because this hand is captured. Records the
    /// prop so the episode's OFF line can NAME what the shell switched off.</summary>
    internal static void NotePropVeto(HandSide side, string propLabel, bool wasInReach)
    {
        HandWatch w = Hands[(int)side];
        w.Vetoes++;
        _sessionVetoes++;
        if (!wasInReach || w.VetoedNames.Count >= VetoNameBudget)
            return;
        if (w.VetoedNames.Add(propLabel))
            w.VetoedProps++;
    }

    /// <summary>Census cadence. Called from <c>FigureStretch.Tick</c>, i.e. from the driver's
    /// Update — which is why the ABSENCE of the census line is itself a reading (see the class
    /// doc).</summary>
    internal static void Tick()
    {
        if (!_censusArmed || Time.unscaledTime < _nextCensus)
            return;
        _nextCensus = Time.unscaledTime + CensusSeconds;
        EmitCensus();
    }

    private static void EmitCaptureOn(HandSide side, HandWatch w)
    {
        if (_episodeLogsLeft <= 0)
            return;
        // HW-VERIFY: the capture state was invisible in every log before ModBuild 400 and it is
        // the state that decides whether a prop can be highlighted or grabbed. Tier is enforced by
        // scripts/check-hw-verify.py.
        VRLog.Note("FigureGrab",
            $"STRETCH CAPTURE ON — {side} hand entered the resize capture shell of the other hand's "
            + $"{w.TargetLabel}: surface distance {w.SurfaceMm:F0} mm real against the "
            + $"{w.ThresholdMm:F0} mm [FigureGrab] StretchReachMillimeters threshold. THE SHELL IS "
            + "DRAWN AROUND THE WIDEST SURVIVING RENDERER, and that is the field this line exists "
            + "for: "
            + (w.BodyRadiusMm < 0f
                ? "NO renderer survived the sanity ceiling, so the test fell back to the CENTRE "
                  + "distance"
                : $"'{w.BodyRenderer}' at {w.BodyRadiusMm:F0} mm implied body radius")
            + $" (ceiling {w.CeilingMm:F0} mm at this hold's total size {w.TotalRatio:0.###}×"
            + (w.CentreFallback ? ", CENTRE fallback in force" : "")
            + "). While this state holds, this hand elects NO figure and EVERY prop's AllowsHand "
            + "answers false — highlight and grab both gone. A body radius of a few centimetres is "
            + "the hand being at the mini; a body radius of hundreds of millimetres is a stray "
            + "effect mesh inflating the AABB, and then the shell is the defect, not the reach dial.");
    }

    private static void EmitCaptureOff(HandSide side, HandWatch w)
    {
        if (_episodeLogsLeft <= 0)
            return;
        _episodeLogsLeft--;
        string vetoed = w.VetoedProps == 0
            ? "no prop was in reach of this hand while it held, so nothing was switched off"
            : $"it vetoed {w.VetoedProps} in-reach prop(s): {string.Join(", ", Names(w))}";
        // HW-VERIFY: the other half of the capture episode — what the shell cost. Tier enforced by
        // scripts/check-hw-verify.py.
        VRLog.Note("FigureGrab",
            $"STRETCH CAPTURE OFF — {side} hand left the shell of {w.TargetLabel} after {w.Frames} "
            + $"frame(s); a resize gesture "
            + (w.GestureStarted ? "DID start inside it" : "never started inside it")
            + $". While it held, {vetoed}. AN EPISODE THAT VETOED PROPS AND STARTED NO GESTURE IS "
            + "THE 2026-09-03 DEFECT, measured: the shell switched off exactly what the player was "
            + $"reaching for. ({_episodeLogsLeft} more capture episodes narrated this session.)");
    }

    private static string[] Names(HandWatch w)
    {
        var names = new string[w.VetoedNames.Count];
        w.VetoedNames.CopyTo(names);
        return names;
    }

    private static void EmitCensus()
    {
        HandWatch l = Hands[0];
        HandWatch r = Hands[1];
        // HW-VERIFY: THE ZERO LINE. It prints on its cadence whether or not anything happened, and
        // is the only place "no hand was captured" is a printed sentence. Tier enforced by
        // scripts/check-hw-verify.py.
        VRLog.Note("FigureGrab",
            $"STRETCH CAPTURE census (last {CensusSeconds:F0}s) — "
            + Row("Left", l) + "; " + Row("Right", r)
            + $". Session totals: {_sessionEpisodes} capture episode(s), {_sessionRefusals} refused "
            + $"by a prop pick, {_sessionOverruled} prop pick(s) overruled by a nearer shell, "
            + $"{_sessionVetoes} prop veto call(s). READING THE TWO PICK COLUMNS (ModBuild 445): "
            + "'refused' and 'overruled' are the two outcomes of ONE comparison — is the prop or "
            + "the held object the nearer surface to this hand. Both large is a player working "
            + "over a crowded board and is healthy. Refused large with 'gestures started 0' is the "
            + "2026-09-05 failure exactly: one prop reporting a distance it should not, vetoing "
            + "every capture; cross-check it against the [Props] DEAD PICK SHAPE line, which names "
            + "any prop registered with a collider whose ClosestPoint cannot be believed. "
            + "READING THE ZEROES: ticks 0 means "
            + "the gesture tick ran but this hand had no pose; 'with a target' 0 means no hand was "
            + "holding anything to resize, so the capture rule had nothing to test; captured 0 with "
            + "a non-zero 'with a target' means THE RULE RAN AND NO HAND WAS EVER CAPTURED. This "
            + "line is emitted from FigureStretch.Tick inside FigureGrabDriver.Update, so its "
            + "complete ABSENCE from a log means the driver itself never ticked — a different "
            + "answer from any zero on it.");
        Reset(l);
        Reset(r);
    }

    private static string Row(string name, HandWatch w)
        => $"{name}: ticks {w.Ticks} ({w.TicksWithTarget} with a stretch target in the other hand), "
           + $"captured {w.Episodes}× over {w.EpisodeFrames} frame(s), gestures started "
           + $"{w.GesturesStarted}, refused by a prop pick {w.Refusals}× "
           + (w.Refusals > 0
               ? $"(last: {w.LastRefusalProp} at {w.LastRefusalMm:F0} mm vs shell "
                 + $"{w.LastRefusalShellMm:F0} mm)"
               : "(none)")
           + $", prop pick OVERRULED (the shell was nearer, so the capture went ahead) "
           + $"{w.Overruled}× "
           + (w.Overruled > 0
               ? $"(last: {w.LastOverruledProp} at {w.LastOverruledMm:F0} mm vs shell "
                 + $"{w.LastOverruledShellMm:F0} mm)"
               : "(none)")
           + $", prop veto calls {w.Vetoes}";

    private static void Reset(HandWatch w)
    {
        w.Ticks = 0;
        w.TicksWithTarget = 0;
        w.Episodes = w.Captured ? 1 : 0;   // an episode still open is still one episode
        w.EpisodeFrames = 0;
        w.Refusals = 0;
        w.Overruled = 0;
        w.Vetoes = 0;
        w.GesturesStarted = 0;
    }
}
