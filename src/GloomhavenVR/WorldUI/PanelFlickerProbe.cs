using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// WHAT KIND OF FLICKER IS IT — the instrument that ends the guessing.
///
/// <para>Three builds have chased "das Flackern" in the 3D map room's floated windows and three
/// hypotheses have been FALSIFIED, each cheaply and each only after it shipped:
/// <list type="number">
/// <item>a per-frame <c>overrideSorting</c> write war (real, fixed in 179 — the log fell from
///   18,994 repeats to zero — and the flicker survived it, so it was never the whole cause);</item>
/// <item>the mod-layer sweep dragging the live character rig out of its preview camera's sight
///   (180) — the very next log says <c>0 subtree(s) LEFT ALONE</c>: there is not a single
///   <c>Renderer</c> inside those windows, so the skip never fired and the theory was empty;</item>
/// <item>the per-frame distance sort thrashing between near-equal panels — it already carries a
///   hysteresis-gated adjacent swap, by design, for exactly that reason.</item>
/// </list>
/// A fourth guess is not worth a hardware round. What is missing is not an idea but a
/// MEASUREMENT: nobody has established whether the two eyes disagree WITHIN a frame (stereo
/// rivalry — a value written between the eye passes) or whether successive frames alternate
/// (a temporal fight). Those two have disjoint causes and disjoint fixes, and one line of log
/// separates them.</para>
///
/// <para>HOW. MultiPass renders the head camera ONCE PER EYE, so <c>Camera.onPreRender</c> fires
/// twice per frame for it. This samples the state that decides what a panel looks like at each of
/// those moments:
/// <list type="bullet">
/// <item>within one frame, pass 0 vs pass 1 → <b>STEREO RIVALRY</b>, and the field that differs;</item>
/// <item>across frames, pass 0 vs the previous frame's pass 0, watching for an A-B-A-B ladder →
///   <b>TEMPORAL ALTERNATION</b>, and the field that alternates.</item>
/// </list>
/// Both verdicts name the PANEL and the FIELD, so the next round starts from "canvas X's
/// sortingOrder alternates" instead of from a theory.</para>
///
/// <para>COST: a few dozen field reads per eye pass over the handful of converted panels, no
/// allocation after warm-up, and one log line per (panel, field) per session. It arms itself only
/// while a converted panel exists.</para>
/// </summary>
internal static class PanelFlickerProbe
{
    private const string Scope = "WorldUI";

    /// <summary>Alternations in a row before a temporal verdict is called. Six frames of A-B-A-B is
    /// not a coincidence and is well under a tenth of a second — the same thing the eye is
    /// reporting as flicker.</summary>
    private const int AlternationRunToReport = 6;

    /// <summary>Everything about one panel that can change what it looks like. Value type: the
    /// comparison is field by field, so the verdict can NAME the field rather than say "something
    /// changed", which is the whole reason a hash was not used.</summary>
    private struct Sample
    {
        public bool Valid;
        public bool HostActive;
        public bool CanvasEnabled;
        public int SortingOrder;
        public bool OverrideSorting;
        public int RenderMode;
        public bool HasWorldCamera;
        public int Layer;
        public Vector3 Pos;
        public Vector3 Scale;
        public int ChildEnabledMask;   // bit i = adopted canvas i enabled
        public int ChildOrderSum;      // cheap composite; a change means SOME child re-sorted
        public int ChildOverrideMask;
    }

    private sealed class Track
    {
        public Sample PassA;
        public Sample PassB;
        public Sample PrevFrameA;
        public Sample PrevPrevFrameA;
        public int AlternationRun;
        public readonly HashSet<string> Reported = new();
    }

    private static readonly Dictionary<int, Track> Tracks = new();
    private static bool _hooked;
    private static int _frame = -1;
    private static int _pass;

    /// <summary>Arm/disarm with the floated-window layer. Idempotent and cheap — called from the
    /// modal tick, which already runs every frame.</summary>
    internal static void Sync(bool wanted)
    {
        if (wanted == _hooked)
            return;
        _hooked = wanted;
        if (wanted)
        {
            Camera.onPreRender += OnPreRender;
            VRLog.Info(Scope, "PANEL FLICKER PROBE armed — sampling every floated panel at BOTH "
                              + "MultiPass eye passes. It prints at most one line per panel per field, "
                              + "and only when the two eyes disagree inside a frame (stereo rivalry) or "
                              + "successive frames alternate (a temporal fight). Silence from here on "
                              + "means the panels' own state is steady and the flicker is downstream of "
                              + "it — which is itself the answer to a question three builds have guessed at.");
        }
        else
        {
            Camera.onPreRender -= OnPreRender;
            Tracks.Clear();
            _frame = -1;
        }
    }

    private static void OnPreRender(Camera cam)
    {
        if (cam == null || !ReferenceEquals(cam, VRRigDriver.HeadCamera))
            return;

        if (Time.frameCount != _frame)
        {
            _frame = Time.frameCount;
            _pass = 0;
        }
        else
        {
            _pass++;
        }

        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
        {
            ConvertedPanel panel = panels[i];
            if (panel == null || panel.HostGo == null)
                continue;
            int id = panel.HostGo.GetInstanceID();
            if (!Tracks.TryGetValue(id, out Track? track))
            {
                track = new Track();
                Tracks[id] = track;
            }
            Sample now = Take(panel);

            if (_pass == 0)
            {
                // A new frame's first eye: judge the PREVIOUS frame's two passes against each
                // other, then the frame-to-frame ladder.
                if (track.PassA.Valid && track.PassB.Valid)
                    JudgeStereo(panel, track);
                JudgeTemporal(panel, track, now);
                track.PrevPrevFrameA = track.PrevFrameA;
                track.PrevFrameA = now;
                track.PassA = now;
                track.PassB = default;
            }
            else if (_pass == 1)
            {
                track.PassB = now;
            }
        }
    }

    private static Sample Take(ConvertedPanel panel)
    {
        var s = new Sample { Valid = true };
        GameObject host = panel.HostGo;
        s.HostActive = host.activeInHierarchy;
        s.Layer = host.layer;
        Transform t = host.transform;
        s.Pos = t.position;
        s.Scale = t.lossyScale;

        Canvas? c = panel.HostCanvas;
        if (c != null)
        {
            s.CanvasEnabled = c.enabled;
            s.SortingOrder = c.sortingOrder;
            s.OverrideSorting = c.overrideSorting;
            s.RenderMode = (int)c.renderMode;
            s.HasWorldCamera = c.worldCamera != null;
        }

        List<NestedCanvasRecord> kids = panel.AdoptedCanvases;
        int n = Mathf.Min(kids.Count, 32);
        for (int i = 0; i < n; i++)
        {
            Canvas? k = kids[i].Canvas;
            if (k == null)
                continue;
            if (k.enabled)
                s.ChildEnabledMask |= 1 << i;
            if (k.overrideSorting)
                s.ChildOverrideMask |= 1 << i;
            s.ChildOrderSum += k.sortingOrder * (i + 1);
        }
        return s;
    }

    private static void JudgeStereo(ConvertedPanel panel, Track track)
    {
        string? field = FirstDifference(track.PassA, track.PassB);
        if (field == null)
            return;
        Report(panel, track, $"stereo:{field}",
            $"PANEL FLICKER — STEREO RIVALRY on '{panel.HostGo.name}': '{field}' has DIFFERENT values "
            + "in the two MultiPass eye passes of the SAME frame. Something writes it between the eye "
            + "renders, so the left and right eyes are shown different pictures and the brain reads "
            + "that as violent flicker. Look for a writer running on a render callback "
            + "(onPreRender/onPreCull/OnWillRenderObject) or for a value derived from the CAMERA POSE "
            + "— the eye offset changes between the passes, so anything computed from the head "
            + "transform at render time is per-EYE by construction.");
    }

    private static void JudgeTemporal(ConvertedPanel panel, Track track, Sample now)
    {
        if (!track.PrevFrameA.Valid || !track.PrevPrevFrameA.Valid)
            return;
        string? changed = FirstDifference(track.PrevFrameA, now);
        if (changed == null)
        {
            track.AlternationRun = 0;
            return;
        }
        // Alternating means: differs from LAST frame but EQUALS the one before it.
        bool alternating = FirstDifference(track.PrevPrevFrameA, now) == null;
        if (!alternating)
        {
            track.AlternationRun = 0;
            return;
        }
        if (++track.AlternationRun < AlternationRunToReport)
            return;
        track.AlternationRun = 0;
        Report(panel, track, $"temporal:{changed}",
            $"PANEL FLICKER — TEMPORAL ALTERNATION on '{panel.HostGo.name}': '{changed}' flips back and "
            + "forth on consecutive frames (A-B-A-B), which is a WRITE WAR: two owners, each undoing "
            + "the other every frame. Both eyes agree within a frame, so this is not stereo — it is "
            + "the mod and the game (or two mod paths) fighting over one value. The fix is never to "
            + "write harder; concede the contested property and own a derived one, the way "
            + "CanvasConversion.ReassertAdoptedSorting concedes overrideSorting.");
    }

    private static string? FirstDifference(Sample a, Sample b)
    {
        if (!a.Valid || !b.Valid)
            return null;
        if (a.HostActive != b.HostActive) return "host.activeInHierarchy";
        if (a.CanvasEnabled != b.CanvasEnabled) return "canvas.enabled";
        if (a.SortingOrder != b.SortingOrder) return "canvas.sortingOrder";
        if (a.OverrideSorting != b.OverrideSorting) return "canvas.overrideSorting";
        if (a.RenderMode != b.RenderMode) return "canvas.renderMode";
        if (a.HasWorldCamera != b.HasWorldCamera) return "canvas.worldCamera (null-ness)";
        if (a.Layer != b.Layer) return "host.layer";
        if (a.ChildEnabledMask != b.ChildEnabledMask) return "an adopted canvas's enabled flag";
        if (a.ChildOverrideMask != b.ChildOverrideMask) return "an adopted canvas's overrideSorting";
        if (a.ChildOrderSum != b.ChildOrderSum) return "an adopted canvas's sortingOrder";
        // Pose last: it moves legitimately while the player drags a window, so it is the weakest
        // signal and must never mask a stronger one above it.
        if ((a.Pos - b.Pos).sqrMagnitude > 1e-6f) return "host.position";
        if ((a.Scale - b.Scale).sqrMagnitude > 1e-10f) return "host.lossyScale";
        return null;
    }

    private static void Report(ConvertedPanel panel, Track track, string key, string message)
    {
        if (!track.Reported.Add(key))
            return;
        VRLog.Warn(Scope, message);
    }
}
