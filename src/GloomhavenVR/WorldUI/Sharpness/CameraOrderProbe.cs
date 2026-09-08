using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE FLICKER, ROUND 5 — MEASURE THE CAMERA RENDER ORDER, AND FIX WHAT THE MEASUREMENT NAMES.
///
/// <para>WHY THIS AND NOT ANOTHER PANEL THEORY. the ModBuild 182 panel-state probe (since removed) (ModBuild 182) ran
/// a full hardware session and printed NOTHING: across every floated panel, neither eye pass
/// disagreed with the other inside a frame nor did successive frames alternate, on canvas enabled,
/// sortingOrder, overrideSorting, renderMode, worldCamera, layer, host activity or host pose. That
/// silence is a RESULT, and it retires the whole family of hypotheses the previous four builds
/// worked through — the panels' own state is steady. What flickers are exactly the two things fed
/// by a CAMERA INTO A RENDERTEXTURE (the live character render and the story picture); the text
/// beside them, drawn by the same canvas at the same sorting order, does not.</para>
///
/// <para>THE MECHANISM THIS LOOKS FOR. MultiPass renders the head camera ONCE PER EYE. A mono
/// camera that draws into its own <c>targetTexture</c> renders on its own schedule, ordered by
/// <c>Camera.depth</c>. If it renders BETWEEN the two eye passes, the left eye samples generation
/// N-1 of that texture and the right eye samples generation N — a still image in each eye, two
/// different images between them, which is stereo rivalry in the TEXTURE rather than in the
/// panel. It is exactly the signature of "the picture flickers but the text next to it does not".
/// The game's character render is <c>'GUI 3D Camera'</c> at <b>depth 24</b> against a head camera
/// at depth 1 — i.e. it is ordered AFTER the head camera, which is where the interleave would
/// come from.</para>
///
/// <para>THE PROBE PRINTS ITS BASELINE EVEN WHEN IT FINDS NOTHING. Every distinct render-order
/// SHAPE is logged once (up to <see cref="MaxShapesLogged"/>), so a log always says what the order
/// actually was — a scan that only speaks up on a hit cannot be told apart from one that never
/// ran, and this project has already paid for that lesson once.</para>
///
/// <para>THE CORRECTION IS DATA-DRIVEN. Nothing is re-ordered on a hunch: a camera is hoisted only
/// after it has been OBSERVED rendering between the two eye passes, and the observation is logged
/// before the correction is applied. The hoist sets the offender's depth just below the head
/// camera's, so it finishes before either eye starts and both eyes sample the same generation.
/// This is safe for exactly the cameras it applies to: a camera with its own
/// <c>targetTexture</c> composites nothing onto the screen, so its depth decides only WHEN it
/// draws, never what ends up in front of what. The original depth is remembered and restored on
/// teardown, and the head camera and the mod's own cameras are never touched.</para>
///
/// <para>THE WRITE HAPPENS IN UPDATE, NEVER IN THE RENDER LOOP. <c>onPreRender</c> is inside the
/// render phase, where the project's own <c>AssertNotInRenderPhase</c> rule says visibility and
/// ordering must not change — a write there would land in one eye and be the very bug class this
/// is chasing. The probe only RECORDS during the render loop; <see cref="TickApply"/> performs
/// the hoist from the modal tick.</para>
/// </summary>
internal static class CameraOrderProbe
{
    private const string Scope = "WorldUI";

    /// <summary>Distinct render-order shapes to log before falling silent — enough to carry the
    /// menu, the map room and a scenario without turning the log into a per-frame dump.</summary>
    private const int MaxShapesLogged = 6;

    private readonly struct Entry
    {
        internal readonly Camera Cam;
        internal readonly bool IsHead;
        internal readonly bool HasTarget;
        internal readonly Camera.MonoOrStereoscopicEye Eye;

        internal Entry(Camera cam, bool isHead, bool hasTarget, Camera.MonoOrStereoscopicEye eye)
        {
            Cam = cam;
            IsHead = isHead;
            HasTarget = hasTarget;
            Eye = eye;
        }
    }

    private static readonly List<Entry> Frame = new(16);
    private static readonly HashSet<string> ShapesLogged = new();
    private static readonly HashSet<int> Reported = new();

    /// <summary>Cameras hoisted below the head, with the depth they had before — restored on disarm.</summary>
    private static readonly Dictionary<Camera, float> Hoisted = new();

    /// <summary>Cameras the last judged frame found between the eye passes, awaiting the Update-phase hoist.</summary>
    private static readonly List<Camera> PendingHoist = new(2);

    private static bool _hooked;
    private static int _frame = -1;
    private static readonly StringBuilder Sb = new(256);

    /// <summary>Arm/disarm alongside the floated-window layer. Idempotent.</summary>
    internal static void Sync(bool wanted)
    {
        if (wanted == _hooked)
            return;
        _hooked = wanted;
        if (wanted)
        {
            Camera.onPreRender += OnPreRender;
            VRLog.Info(Scope, "CAMERA ORDER PROBE armed — recording the per-frame camera render "
                              + "sequence and judging whether any RenderTexture camera draws BETWEEN the "
                              + "head camera's two MultiPass eye passes. It logs each distinct order "
                              + "SHAPE once (so the log says what the order was even when nothing is "
                              + "wrong), and names any offender before hoisting it below the head. This "
                              + "is the measurement the panel-state probe's silence pointed at: the panels' "
                              + "state is steady, so the flicker is in the texture, not the arrangement.");
        }
        else
        {
            Camera.onPreRender -= OnPreRender;
            RestoreAll("probe disarmed");
            Frame.Clear();
            PendingHoist.Clear();
            _frame = -1;
        }
    }

    private static void OnPreRender(Camera cam)
    {
        if (cam == null)
            return;
        Camera? head = VRRigDriver.HeadCamera;
        if (head == null)
            return;

        if (Time.frameCount != _frame)
        {
            Judge(head);
            _frame = Time.frameCount;
            Frame.Clear();
        }
        if (Frame.Count < 32)
        {
            Frame.Add(new Entry(cam, ReferenceEquals(cam, head), cam.targetTexture != null,
                                cam.stereoActiveEye));
        }
    }

    /// <summary>
    /// Judge the frame that just ended. Two questions, in this order: what WAS the sequence (logged
    /// once per distinct shape), and did anything with a targetTexture render strictly between the
    /// head camera's first and last pass.
    /// </summary>
    private static void Judge(Camera head)
    {
        if (Frame.Count == 0)
            return;

        int first = -1;
        int last = -1;
        for (int i = 0; i < Frame.Count; i++)
        {
            if (!Frame[i].IsHead)
                continue;
            if (first < 0)
                first = i;
            last = i;
        }

        LogShape(head, first, last);

        // One head pass (mono, or the frame was cut short) cannot interleave with itself.
        if (first < 0 || last <= first)
            return;

        for (int i = first + 1; i < last; i++)
        {
            Entry e = Frame[i];
            if (e.IsHead || !e.HasTarget || e.Cam == null)
                continue;
            if (IsOurs(e.Cam) || Hoisted.ContainsKey(e.Cam))
                continue;
            int id = e.Cam.GetInstanceID();
            if (!Reported.Add(id))
                continue;
            // HW-VERIFY (2026-09 refactor, F-68) — the observation this probe exists to make,
            // and the justification printed BEFORE the correction is applied. One-shot per camera
            // (Reported is an instance-id set).
            VRLog.Alert(Scope, $"CAMERA ORDER: '{e.Cam.name}' renders INTO A RENDERTEXTURE "
                              + $"('{TargetName(e.Cam)}') BETWEEN the head camera's two MultiPass eye "
                              + $"passes (depth {e.Cam.depth:F1} against the head's {head.depth:F1}, "
                              + $"stereo target {e.Cam.stereoTargetEye}, mask 0x{e.Cam.cullingMask:X8}). "
                              + "THE LEFT EYE THEREFORE SAMPLES ONE GENERATION OF THAT TEXTURE AND THE "
                              + "RIGHT EYE THE NEXT — a still picture in each eye and a different one "
                              + "between them, which is what a flickering image beside non-flickering "
                              + "text looks like. Hoisting it below the head camera on the next Update "
                              + "so it finishes before either eye starts.");
            PendingHoist.Add(e.Cam);
        }
    }

    /// <summary>Log each distinct render-order shape once — the baseline that proves the probe ran.</summary>
    private static void LogShape(Camera head, int first, int last)
    {
        Sb.Length = 0;
        for (int i = 0; i < Frame.Count; i++)
        {
            Entry e = Frame[i];
            if (i > 0)
                Sb.Append(" → ");
            Sb.Append(e.Cam != null ? e.Cam.name : "<gone>");
            if (e.IsHead)
                Sb.Append('[').Append(e.Eye).Append(']');
            else if (e.HasTarget)
                Sb.Append("[→RT]");
        }
        string shape = Sb.ToString();
        if (!ShapesLogged.Add(shape))
            return;
        if (ShapesLogged.Count > MaxShapesLogged)
            return;
        int between = 0;
        if (first >= 0 && last > first)
        {
            for (int i = first + 1; i < last; i++)
            {
                if (!Frame[i].IsHead)
                    between++;
            }
        }
        // HW-VERIFY (2026-09 refactor, F-68) — THE BASELINE THE CLASS DOC PROMISES IN CAPITALS:
        // "a scan that only speaks up on a hit cannot be told apart from one that never ran, and
        // this project has already paid for that lesson once". At Info it printed in no shipped
        // log, so the promise was not kept for any hardware session. Capped at MaxShapesLogged.
        VRLog.Note(Scope, $"CAMERA ORDER (shape {ShapesLogged.Count}/{MaxShapesLogged}): {shape} — "
                          + $"{(first >= 0 && last > first ? $"{between} camera(s) render BETWEEN the head's two eye passes"
                              : "the head camera does not render twice in this frame (mono, or the frame was cut short)")}. "
                          + $"Head '{head.name}' depth {head.depth:F1}. Every distinct shape is logged once; "
                          + "this line exists so a quiet log still says what the order actually was.");
    }

    /// <summary>
    /// Apply any hoist the last judged frame asked for. Called from the modal tick — i.e. from
    /// Update, never from inside the render loop.
    /// </summary>
    internal static void TickApply()
    {
        if (PendingHoist.Count == 0)
            return;
        Camera? head = VRRigDriver.HeadCamera;
        for (int i = 0; i < PendingHoist.Count; i++)
        {
            Camera cam = PendingHoist[i];
            if (cam == null || head == null || Hoisted.ContainsKey(cam))
                continue;
            float was = cam.depth;
            float now = head.depth - 1f;
            if (was <= now)
                continue; // already ahead of the head — nothing to correct
            Hoisted[cam] = was;
            cam.depth = now;
            // HW-VERIFY (2026-09 refactor, F-68) — THIS LINE REPORTS A WRITE TO A GAME CAMERA.
            // The mod re-orders the game's render sequence here; a change to game state with no
            // record in the player's log is a change nobody can audit. Once per camera.
            VRLog.Note(Scope, $"CAMERA ORDER: '{cam.name}' depth {was:F1} → {now:F1} (head "
                              + $"{head.depth:F1}) — it now finishes its RenderTexture BEFORE either eye "
                              + "pass starts, so both eyes sample the same generation. Safe by "
                              + "construction: a camera with its own targetTexture composites nothing "
                              + "onto the screen, so its depth decides only WHEN it draws. The original "
                              + "depth is remembered and restored when the probe stands down.");
        }
        PendingHoist.Clear();
    }

    private static void RestoreAll(string why)
    {
        if (Hoisted.Count == 0)
        {
            Reported.Clear();
            ShapesLogged.Clear();
            return;
        }
        int n = 0;
        foreach (KeyValuePair<Camera, float> kv in Hoisted)
        {
            if (kv.Key == null)
                continue;
            kv.Key.depth = kv.Value;
            n++;
        }
        Hoisted.Clear();
        Reported.Clear();
        ShapesLogged.Clear();
        // HW-VERIFY (2026-09 refactor, F-68) — the other half of the write above; the two must be
        // readable in the same log or a restore cannot be confirmed against its hoist.
        VRLog.Note(Scope, $"CAMERA ORDER: {n} camera depth(s) restored ({why}) — the game owns its "
                          + "render order again.");
    }

    /// <summary>Never re-order the mod's own cameras (head, mirrors, preview stations, sinks).</summary>
    private static bool IsOurs(Camera cam) =>
        cam.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal);

    private static string TargetName(Camera cam)
    {
        RenderTexture? rt = cam.targetTexture;
        return rt != null ? rt.name : "<none>";
    }
}
