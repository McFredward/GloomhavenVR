using System.Diagnostics;
using System.Text;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// NAMES WHAT IS (OR IS NOT) DRAWN IN THE VIEW CONE, in one report, without hardware.
///
/// WHY THIS EXISTS. ModBuild 229 shipped a 3D map room in which the user saw "ein großer
/// schwarzer Rahmen um den Spieler ... dass man garnicht den Sternenhimmel sehen kann - fliegt
/// man weiter weg, verdeckt auch teile des Tischs. Es folgt den Kopfbewegungen". Sampling
/// .planning/debug/3dmap_schwarzer_block.mp4 gave hard RGB(0,0,0) with a boundary that moved with
/// the view — and that measurement is compatible with TWO completely different worlds:
///
///   (1) a renderer is drawing black there (an over-sized enclosure, a shell, a backdrop quad), or
///   (2) NOTHING is drawn there and the head camera's SolidColor [Rig] VoidColor clear is showing.
///
/// Those two demand opposite fixes and no photograph can separate them, because a black fill and a
/// black clear are the same pixels. Every instrument this project already had measured STATE — a
/// renderer's enabled flag, a material's queue, a transform's scale — and state cannot answer
/// "what reached this pixel". The actual cause was (2), and it was caused by a number that no log
/// line in a 22 MB Player.log printed after the environment came up: the head camera's FAR CLIP
/// PLANE, stuck at the map rig's seeded 1000 world units while the rig ran at scale 198.12 — a
/// clip plane 5.05 PERCEIVED METRES in front of his eyes. See SkyAlternative.MinFarWorldUnits for
/// the mechanism and SkyAlternative.Tick's ACTIVATION note for why the flag it depended on was
/// never set.
///
/// WHAT IT MEASURES. A fan of <see cref="Columns"/> × <see cref="Rows"/> rays cast through the head
/// camera's own viewport, and for each ray the nearest renderer whose WORLD BOUNDS the ray enters.
/// Per ray it prints the renderer's name, its full hierarchy path, its layer (number and name),
/// its shader, its first material's render queue, its bounds size in BOTH world units and perceived
/// metres, the hit distance in both, and whether the head camera's culling mask contains that
/// layer. For a ray with NO drawable candidate it says so explicitly and names the camera's
/// clearFlags and clear colour, because "no hit" is the finding that means the clear is showing.
/// It then splits "no drawable candidate" into the three reasons it can actually distinguish:
/// BEYOND THE FAR PLANE, NOT IN THE CULLING MASK, and NOTHING AT ALL ALONG THIS RAY.
///
/// WHAT IT DOES NOT MEASURE, stated here so no future round reads more into a line than is in it:
///  - IT DOES NOT USE PHYSICS. <c>SkyAlternative.StripColliders</c> destroys every collider under
///    both environment branches at spawn ("Nicht interaktiv rein als Umgebung"), so a
///    <c>Physics.Raycast</c> fan through a bundled room reports NO HIT for every ray and would
///    "prove" the clear is showing in a room packed with trees. That trap is why this walks
///    renderers instead, and it is the single most important sentence in this file.
///  - IT TESTS AXIS-ALIGNED BOUNDS, NOT TRIANGLES. A ray that enters a renderer's AABB may miss
///    every triangle in it. So a HIT means "this renderer's bounds are in the way", never "this
///    renderer painted that pixel". A NO-HIT is the strong direction: if nothing's bounds are in
///    the way, nothing was drawn, full stop.
///  - IT DOES NOT READ THE FRAMEBUFFER, so it cannot say what colour anything is. It says what
///    geometry is reachable and what the camera would clear to where none is.
///  - IT ASSERTS NO MECHANISM. Two verdicts in this project's history stated causes their own
///    instrument could not observe and both were false for a whole session. Every clause below is
///    a direct read of a Unity field or a count of the rows above it.
///
/// WHEN IT RUNS. Never on a cadence.
///  - ONCE per <see cref="Arm"/>, one frame later so the placement and the clip planes have
///    settled (VRRigDriver's 'Rig.ClipPlanes' step runs BEFORE 'Rig.MixedReality', so the far
///    plane derived from a fresh activation lands on the NEXT frame, not this one).
///    SkyAlternative arms it when an environment activates.
///  - AND on the WATCHDOG: <see cref="TickWatchdog"/> is two float reads and a compare per frame,
///    and fires a report only when the live far plane fails to cover the budget
///    <c>SkyAlternative.MinFarWorldUnits</c> just asked for. That is the exact condition that was
///    true for 19,853 consecutive frames of his log with nothing anywhere saying so. It is capped
///    at <see cref="MaxWatchdogReports"/> per session and rate-limited to
///    <see cref="WatchdogCooldownSeconds"/>, because a watchdog for a persistent fault would
///    otherwise be a log flood, and a flood is how the last one got ignored.
///
/// COST. One <c>Object.FindObjectsOfType&lt;Renderer&gt;()</c> per report — the sweep this project
/// treats as the default suspect, and correctly: it has been the single biggest frame cost found
/// three times. It is acceptable HERE and only here because it is a ONE-SHOT on a report frame and
/// never on a cadence, and because the report PRINTS ITS OWN MEASURED MILLISECONDS so the next
/// round can judge it with a number instead of an assumption. No allocation and no sweep happens on
/// a frame that does not report.
///
/// MULTIPLAYER: reads only, writes nothing, sends nothing. Head-pose independent in the sense that
/// matters to the standing ruling — it never moves, re-orients or re-parents anything.
/// </summary>
internal static class ViewConeProbe
{
    /// <summary>Ray fan width across the viewport. 5 × 3 = 15 rays: enough to separate "the middle
    /// of the view is empty and the edges are not" (which is what his video shows) from "everything
    /// is empty", and small enough that the report is one screen of log.</summary>
    private const int Columns = 5;

    /// <summary>Ray fan height across the viewport. See <see cref="Columns"/>.</summary>
    private const int Rows = 3;

    /// <summary>Viewport inset for the outermost rays, so the fan samples inside the frame rather
    /// than exactly on its edge where a single-pixel sliver decides the answer.</summary>
    private const float ViewportInset = 0.08f;

    /// <summary>Ignore renderers with a degenerate world AABB: a zero-size bound never intersects a
    /// ray anyway, and reporting one as "the nearest thing in the way" would be a lie.</summary>
    private const float MinBoundsExtent = 1e-4f;

    /// <summary>Session cap on WATCHDOG reports. The arm-driven report is separate and is not
    /// capped, because it fires once per activation by construction.</summary>
    private const int MaxWatchdogReports = 4;

    /// <summary>Rate limit between watchdog reports, unscaled seconds.</summary>
    private const float WatchdogCooldownSeconds = 20f;

    /// <summary>How much slack the far plane gets against the requested budget before the watchdog
    /// calls it uncovered. VRRigDriver.TickClipPlanes writes the far plane through a
    /// <c>Mathf.Approximately</c> guard and caps it at <c>near × MaxFarNearRatio</c>, so an exact
    /// equality test would fire on rounding. 1 ‰ is far below any real shortfall — his was 5 % of
    /// what the environment needed.</summary>
    private const float CoverageTolerance = 0.999f;

    /// <summary>Hierarchy path depth. Deep enough to name the branch root a node hangs under
    /// (that IS the identification), bounded so one pathological hierarchy cannot produce a
    /// kilometre of log.</summary>
    private const int MaxPathDepth = 8;

    private static int _armedFrame = int.MinValue;
    private static string _armedWhy = "";
    private static int _watchdogReports;
    private static float _nextWatchdogTime;

    /// <summary>Reusable builders — the report is a one-shot, but a static builder keeps a report
    /// frame from allocating a fresh few KB on top of the sweep it already pays for.</summary>
    private static readonly StringBuilder Report = new(4096);
    private static readonly StringBuilder PathBuf = new(128);

    /// <summary>
    /// Ask for ONE report on the next frame. Idempotent within a frame; a second arm before the
    /// first has fired simply replaces the reason string, so a burst of activations produces one
    /// report and not five.
    /// </summary>
    internal static void Arm(string why)
    {
        _armedFrame = Time.frameCount;
        _armedWhy = why;
    }

    /// <summary>
    /// Forget everything, on environment teardown. The watchdog budget is deliberately NOT reset:
    /// it is a SESSION cap, and a fault that survives a teardown/rebuild cycle is the same fault.
    /// Resetting the budget here is how a rate limiter becomes a flood.
    /// </summary>
    internal static void Disarm()
    {
        _armedFrame = int.MinValue;
        _armedWhy = "";
    }

    /// <summary>
    /// The per-frame half. Steady state: one int compare, one null check, one call into
    /// <c>SkyAlternative.MinFarWorldUnits</c> (which early-outs on two reference compares when no
    /// environment is spawned) and one float compare. Nothing allocates unless a report fires.
    /// </summary>
    internal static void TickWatchdog(float rigScale)
    {
        Camera? head = VRRigDriver.HeadCamera;
        if (head == null)
            return;

        // ARMED: fire one frame after the arm, so the clip planes written by 'Rig.ClipPlanes' on
        // the frame AFTER the activation are the ones this report reads. Reporting on the arming
        // frame itself would print the far plane the environment had before it existed and would
        // make a healthy build look broken — one step too early is the failure mode this project
        // has already paid for once.
        if (_armedFrame != int.MinValue && Time.frameCount > _armedFrame)
        {
            string why = _armedWhy;
            Disarm();
            Fire(head, rigScale, why, watchdog: false);
            return;
        }

        float need = SkyAlternative.MinFarWorldUnits(rigScale);
        if (need <= 0f)
            return; // no environment spawned — nothing to cover, nothing to check
        if (head.farClipPlane >= need * CoverageTolerance)
            return; // covered: the overwhelmingly common case, and it costs one compare

        if (_watchdogReports >= MaxWatchdogReports || Time.unscaledTime < _nextWatchdogTime)
            return;
        _watchdogReports++;
        _nextWatchdogTime = Time.unscaledTime + WatchdogCooldownSeconds;
        Fire(head, rigScale,
             $"THE FAR PLANE DOES NOT COVER THE ENVIRONMENT (watchdog {_watchdogReports}/{MaxWatchdogReports})",
             watchdog: true);
    }

    /// <summary>
    /// Build and print the report. Everything expensive lives here and here only.
    /// </summary>
    private static void Fire(Camera head, float rigScale, string why, bool watchdog)
    {
        var clock = Stopwatch.StartNew();

        float scale = rigScale > 1e-6f ? rigScale : 1f;
        float near = head.nearClipPlane;
        float far = head.farClipPlane;
        int mask = head.cullingMask;
        float need = SkyAlternative.MinFarWorldUnits(rigScale);

        // THE SWEEP. One pass, one-shot, and its cost is printed below. FindObjectsOfType returns
        // components on ACTIVE GameObjects only, which is what we want: an inactive branch draws
        // nothing and must not be offered as an explanation for a pixel.
        Renderer[] all = Object.FindObjectsOfType<Renderer>();
        long sweepTicks = clock.ElapsedTicks;

        int considered = 0;
        int noCandidate = 0, beyondFar = 0, beforeNear = 0, offMask = 0, drawable = 0;

        Report.Length = 0;
        Report.Append("VIEW CONE PROBE — ").Append(why).Append('.');

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                float vx = Columns == 1 ? 0.5f
                    : Mathf.Lerp(ViewportInset, 1f - ViewportInset, col / (float)(Columns - 1));
                float vy = Rows == 1 ? 0.5f
                    : Mathf.Lerp(ViewportInset, 1f - ViewportInset, row / (float)(Rows - 1));
                Ray ray = head.ViewportPointToRay(new Vector3(vx, vy, 0f));

                // Nearest candidate along this ray, WITHOUT applying mask or clip planes: we want
                // to know what is there before we ask whether the camera would have drawn it.
                // That split is the whole point — "nothing there" and "there but clipped" are
                // different bugs with different fixes, and a probe that pre-filters by the camera's
                // own rules can only ever report the first one.
                Renderer? best = null;
                float bestDist = float.MaxValue;
                bool bestInside = false;
                for (int i = 0; i < all.Length; i++)
                {
                    Renderer r = all[i];
                    if (r == null || !r.enabled)
                        continue;
                    Bounds b = r.bounds;
                    if (b.size.x < MinBoundsExtent && b.size.y < MinBoundsExtent && b.size.z < MinBoundsExtent)
                        continue;
                    if (row == 0 && col == 0)
                        considered++;
                    if (!b.IntersectRay(ray, out float d))
                        continue;
                    bool inside = b.Contains(ray.origin);
                    if (inside)
                        d = 0f;      // Unity's IntersectRay returns a signed distance for an interior origin
                    if (d >= bestDist)
                        continue;
                    best = r;
                    bestDist = d;
                    bestInside = inside;
                }

                Report.Append("\n  ray ").Append(col).Append(',').Append(row)
                      .Append(" viewport (").Append(vx.ToString("F2")).Append(',')
                      .Append(vy.ToString("F2")).Append("): ");

                if (best == null)
                {
                    noCandidate++;
                    Report.Append("NO RENDERER'S BOUNDS ANYWHERE ALONG THIS RAY — nothing is drawn "
                                  + "here and the camera's clear is what reaches the eye (clearFlags ")
                          .Append(head.clearFlags).Append(", clear colour ")
                          .Append(Fmt(head.backgroundColor)).Append(").");
                    continue;
                }

                bool inMask = (mask & (1 << best.gameObject.layer)) != 0;
                bool tooFar = bestDist > far;
                bool tooNear = !bestInside && bestDist < near;
                if (!inMask) offMask++;
                else if (tooFar) beyondFar++;
                else if (tooNear) beforeNear++;
                else drawable++;

                Material? mat = best.sharedMaterial;
                Bounds bb = best.bounds;
                Report.Append(inMask && !tooFar && !tooNear
                                  ? "DRAWABLE"
                                  : !inMask
                                      ? "NOT IN THE CULLING MASK — the camera never sees it, so the clear shows"
                                      : tooFar
                                          ? "BEYOND THE FAR PLANE — CLIPPED AWAY, so the clear shows"
                                          : "IN FRONT OF THE NEAR PLANE — clipped away, so the clear shows")
                      .Append(" | '").Append(best.name).Append("' at ")
                      .Append(bestInside ? "0.0 wu (THE EYE IS INSIDE ITS BOUNDS)"
                                         : bestDist.ToString("F1") + " wu = "
                                           + (bestDist / scale).ToString("F2") + " m perceived")
                      .Append(" | path ").Append(Path(best.transform))
                      .Append(" | layer ").Append(best.gameObject.layer).Append(" '")
                      .Append(LayerMask.LayerToName(best.gameObject.layer)).Append("' ")
                      .Append(inMask ? "IN" : "NOT IN").Append(" the head mask 0x")
                      .Append(mask.ToString("X8"))
                      .Append(" | shader '").Append(mat != null && mat.shader != null ? mat.shader.name : "<none>")
                      .Append("' queue ").Append(mat != null ? mat.renderQueue : -1)
                      .Append(" | bounds ").Append(Fmt(bb.size)).Append(" wu = ")
                      .Append(Fmt(bb.size / scale)).Append(" m perceived");
            }
        }

        clock.Stop();
        double ms = clock.Elapsed.TotalMilliseconds;
        double sweepMs = sweepTicks * 1000.0 / Stopwatch.Frequency;
        int rays = Columns * Rows;

        Report.Append("\n  camera   : '").Append(head.name).Append("' clearFlags ").Append(head.clearFlags)
              .Append(", clear colour ").Append(Fmt(head.backgroundColor))
              .Append(" — THIS IS THE COLOUR EVERY 'the clear shows' ROW ABOVE PUTS ON THE SCREEN. ")
              .Append("near ").Append(near.ToString("F2")).Append(" wu = ")
              .Append((near / scale * 1000f).ToString("F1")).Append(" mm perceived; far ")
              .Append(far.ToString("F0")).Append(" wu = ")
              .Append((far / scale).ToString("F1")).Append(" m perceived; rig scale ")
              .Append(scale.ToString("F2")).Append(" world units per perceived metre.");

        Report.Append("\n  far plane: the environment asks for ").Append(need.ToString("F0"))
              .Append(" wu = ").Append((need / scale).ToString("F1")).Append(" m perceived ")
              .Append("(SkyAlternative.MinFarWorldUnits: the star dome at its authored metres × rig "
                      + "scale, or the placed room's total world extent, whichever is larger, plus "
                      + "the head's distance to that branch's origin). The camera has ")
              .Append(far.ToString("F0")).Append(" wu, which ")
              .Append(need <= 0f
                          ? "is not constrained because no environment is spawned"
                          : far >= need * CoverageTolerance
                              ? "COVERS IT"
                              : "IS SHORT BY " + (need - far).ToString("F0") + " wu = "
                                + ((need - far) / scale).ToString("F2") + " m perceived")
              .Append(". A short far plane is not a dark room: it is a HARD ")
              .Append(Fmt(head.backgroundColor))
              .Append(" wall at exactly that distance in every direction the player looks, and it "
                      + "cuts straight through anything that straddles it — which is what a table "
                      + "sliced in half looks like.");

        Report.Append("\n  VERDICT, ONE LINE (counts, not causes): ").Append(rays).Append(" ray(s) — ")
              .Append(drawable).Append(" with a DRAWABLE renderer, ")
              .Append(beyondFar).Append(" whose nearest renderer is BEYOND THE FAR PLANE, ")
              .Append(offMask).Append(" whose nearest renderer is NOT IN THE CULLING MASK, ")
              .Append(beforeNear).Append(" clipped by the near plane, ")
              .Append(noCandidate).Append(" with NO renderer's bounds along them at all. ")
              .Append(drawable == rays
                          ? "Every ray has something to draw — if the player still reports black, "
                            + "it is a SHADED-BLACK SURFACE and the shader/queue columns above name "
                            + "the candidates; this probe cannot read colour and does not claim to."
                          : "The rows above name, per ray, WHY nothing reaches the eye. Read the "
                            + "far-plane row before the geometry rows: a clip plane produces exactly "
                            + "this pattern and produces it in every direction at once.")
              .Append(" MEASURED COST ").Append(ms.ToString("F2")).Append(" ms total, of which ")
              .Append(sweepMs.ToString("F2")).Append(" ms was the one Object.FindObjectsOfType<Renderer>() "
                      + "sweep over ").Append(all.Length).Append(" renderer(s) (")
              .Append(considered).Append(" with usable bounds); ")
              .Append(rays).Append(" × AABB tests did the rest. ONE-SHOT — this does not run again "
                      + "until the environment is re-activated or the watchdog trips.");

        Report.Append("\n  what this CANNOT say: it tests AXIS-ALIGNED BOUNDS, not triangles, so a "
                      + "hit means 'this renderer is in the way', never 'this renderer painted that "
                      + "pixel'; and it deliberately does NOT use Physics.Raycast, because "
                      + "SkyAlternative strips every collider under both environment branches at "
                      + "spawn and a physics fan would report NO HIT inside a forest.");

        // THE WATCHDOG BRANCH IS THE ONLY ONE A DEFAULT LOG CARRIES, AND UNTIL 2026-09-05 IT WAS
        // NOT. VRLog.Warn and VRLog.Info gate IDENTICALLY on Level >= VRLogLevel.Debug (VRLog.cs,
        // the ModBuild 331 mapping): the two branches differed in the BepInEx severity glyph and
        // in nothing about whether the line appears. So at the shipped LogLevel = Info the
        // watchdog fired, spent one of its four MaxWatchdogReports, and printed nothing — for the
        // fault this class's own doc says "was true for 19,853 consecutive frames of his log with
        // nothing anywhere saying so". The escalation existed and was inert.
        //
        // VRLog.Alert, not VRLog.Note: a far plane that does not cover the environment is a hard
        // background-coloured wall cutting through whatever straddles it, which is a thing the
        // PLAYER sees and can act on (it is a graphics-preset / environment-budget fault), and
        // that is the documented definition of Alert. The ARMED branch stays at Info — it is the
        // one-shot self-report of a healthy activation and nobody is waiting on it.
        if (watchdog)
            // HW-VERIFY: the far-plane watchdog's verdict. Its ABSENCE from a log in which the
            // player reports a sliced table or a black wall is now evidence: this line survives
            // the default level, so silence means the far plane covered the budget every frame.
            VRLog.Alert("Core", Report.ToString());
        else
            VRLog.Info("Core", Report.ToString());
    }

    private static string Path(Transform t)
    {
        PathBuf.Length = 0;
        PathBuf.Append(t.name);
        Transform? p = t.parent;
        for (int guard = 0; p != null && guard < MaxPathDepth; guard++, p = p.parent)
            PathBuf.Insert(0, p.name + "/");
        return PathBuf.ToString();
    }

    private static string Fmt(Vector3 v) =>
        $"({v.x:F2},{v.y:F2},{v.z:F2})";

    private static string Fmt(Color c) =>
        $"RGBA({c.r:F3},{c.g:F3},{c.b:F3},{c.a:F3})";
}
