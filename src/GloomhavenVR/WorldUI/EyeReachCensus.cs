using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE LEFT RIM — A TRIGGERED, WHOLE-SCENE CENSUS OF EVERYTHING THAT CAN PAINT INTO AN EYE.</b>
///
/// <para>THE SYMPTOM, five rounds running, most recently on ModBuild 422:
/// <i>"Das Problem mit dem linken Streifen ist nach wie vor da."</i> With his screenshot
/// (<c>.planning/debug/linkes_element.png</c>):
/// <i>"Wenn man schnell hintereinander die Optionstaste drückt erscheint am linken Rand des auges so
/// ein Rand der dem Kopf folgt statt dem Optionsmenu"</i> and <i>"man sieht das das ursprüngliche
/// graue overlay an der Stelle an der in flat das Optionsmenu wäre angezeigt wird … in VR aus dem
/// linken augenwinkel und bewegt sich mit dem Kopf mit."</i></para>
///
/// <para>THE PICTURE, measured off that screenshot: a uniform, translucent dark-grey, LEFT-ANCHORED
/// column spanning the FULL frame height, hard right edge at 23 % of the width (467 px of a 2000 px
/// wide render), no content inside it, the scene faintly visible through it. It follows the head,
/// and <c>] ITEM9 desktop mirror mode = LEFT EYE</c> proves the monitor shows the left eye, so it is
/// genuinely one-eyed.</para>
///
/// <para><b>WHY THIS IS A MEASUREMENT AND NOT A FIFTH FIX.</b> Four consecutive builds shipped four
/// plausible, well-instrumented fixes for this one symptom and the next hardware log falsified every
/// one. Do not re-derive any of them:</para>
/// <list type="number">
///   <item><b>ModBuild 419</b> — the pre-convert blackout's 8-frame budget expiring. Next log:
///     0 expiries, 24 healthy hand-backs. FALSIFIED.</item>
///   <item><b>ModBuild 420</b> — a tracked window's canvas enabled while un-floated (the
///     "float-intent invariant"). Next log: 0 violations. FALSIFIED.</item>
///   <item><b>ModBuild 421</b> — a PanelSupersample capture straddling the window's next Show,
///     stranding it on capture layer 26. Next log: the straddle gone and
///     <c>] PANEL SUPERSAMPLE LAYER RESTORE REFUSED</c> — the only discriminating line — 0.
///     FALSIFIED. Layers are inert here anyway: an overlay canvas is composited after the camera
///     loop, so there is no culling mask to fail.</item>
///   <item><b>ModBuild 422</b> — the ESC menu's own canvas left enabled on the overlay path by a
///     release firing inside the game's next <c>Show()</c>. That had a real counted asymmetry behind
///     it (10 releases, 9 hides) and it is fixed — but it was NOT the rim, and the 422 log says so:
///     <c>] MODAL OVERLAY RULE</c> fires 0 times; <c>] MODAL 2D HOME CENSUS</c> fires 40 times and
///     across all 40 there is exactly ONE canvas with <c>enabled=True</c>, at
///     <c>alphaAtCanvas=0.00</c>; and the ESC menu itself reads <c>mode=ScreenSpaceCamera
///     cam=UI Camera enabled=False … → REACHES AN EYE: no — drawn by 'UI Camera', which renders into
///     'GloomhavenVR.DesktopScrubSink'</c>. FALSIFIED.</item>
/// </list>
///
/// <para><b>SO THE ARTIFACT IS NOT A CANVAS OF A TRACKED WINDOW.</b> Every instrument so far reported
/// the modal subsystem's OWN BOOKKEEPING, and every reading was true and useless — the failure mode
/// that has now cost four builds ([[measure-the-picture-not-the-state]],
/// [[the-blind-spot-is-the-lead]]). The previous lane named its own blind spots and they are the
/// whole remaining search space: a canvas OUTSIDE the tracked window's subtree, anything the head
/// camera draws from a WorldSpace or ScreenSpaceCamera canvas — and everything nobody has enumerated
/// at all. This class enumerates THE SCENE.</para>
///
/// <para><b>WHAT IT PRINTS</b> — one line each. Anchor every grep on <c>] EYE CENSUS</c>: the <c>] </c>
/// comes from the <c>[WorldUI]</c> scope tag, and it is what stops a count from picking up this mod's
/// prose about its own tokens, which has produced a wrong count three times in this investigation
/// ([[a-summary-stat-is-not-the-field]]).</para>
/// <list type="bullet">
///   <item><c>] EYE CENSUS RUN</c> — the header: why it fired, the tap chain, the head camera, the
///     eye texture size, the mode.</item>
///   <item><c>] EYE CENSUS CAM</c> — EVERY camera in the scene (including inactive ones): depth,
///     <c>targetTexture</c> (null = composites to the display), <c>cullingMask</c>,
///     <c>stereoTargetEye</c>, <c>rect</c>, clear flags and background colour — plus, stated per row,
///     whether it paints into an eye and whether its own <c>rect</c> IS the left band. Nobody in this
///     investigation has looked at <c>Camera.rect</c>, and a camera whose viewport rect is the left
///     fifth of the frame, clearing to a translucent dark colour, would draw exactly the picture.
///     (<c>WorldUI/FlatScreen/CameraInventory.cs</c> prints a similar inventory, but at
///     <c>VRLog.Info</c> — the DEBUG tier, invisible at the shipped default — and only once per scene
///     load while in <c>Menu2D</c>, so it can never coincide with a burst.)</item>
///   <item><c>] EYE CENSUS CANVAS</c> — every canvas that could paint, and every canvas whose rect
///     matches the shape whether it could paint or not: render mode, enabled, active,
///     <c>worldCamera</c> and that camera's <c>targetTexture</c>, sorting order/layer,
///     <c>overrideSorting</c>, GameObject layer, the brightest alpha actually reaching its graphics
///     (engine truth, <c>CanvasRenderer.GetInheritedAlpha()</c>), its normalised screen rect, its full
///     scene path, and whether it is mod-owned or the game's. The rest are counted, not dropped.</item>
///   <item><c>] EYE CENSUS GRAPHIC</c> — the discriminating rows. A canvas is usually full-screen
///     while the BAND is one child <c>Image</c> inside it, so every drawable graphic under every
///     painting root canvas is shape-tested and the matches are named with their path, colour and
///     rect.</item>
///   <item><c>] EYE CENSUS RENDERER</c> — a 3D renderer on a head-visible layer whose screen-projected
///     bounds cover a tall left band. No instrument in this mod has ever looked for this, and it is
///     exactly the shape of a head-parented quad. Matching rows print the LEFT-eye and RIGHT-eye
///     rects separately, which is the direct test of "aus dem linken augenwinkel".</item>
///   <item><c>] EYE CENSUS HEADCHILD</c> — what is glued to the head camera transform right now.</item>
///   <item><c>] EYE CENSUS VERDICT</c> — the ranked answer: how many rows matched the picture, the
///     measured cost, and — stated, not implied — what this census still could NOT see.</item>
/// </list>
///
/// <para><b>COST.</b> This is a scene sweep, and this project has been bitten repeatedly by per-frame
/// <c>FindObjectsOfType</c> ([[findobjectsoftype-is-the-default-suspect]],
/// [[one-line-owned-the-frame]]). It is therefore TRIGGERED and BOUNDED: it never runs from a
/// per-frame path, it runs at most <see cref="MaxRuns"/> times per session, and every run states its
/// own measured milliseconds on the verdict line rather than asserting it is cheap.</para>
///
/// <para><b>MULTIPLAYER.</b> Local diagnostics only. It reads the scene and writes nothing but its own
/// trigger counters — no wire field, no game-state write, no config key.</para>
/// </summary>
internal static class EyeReachCensus
{
    // ---- THE TRIGGER ---------------------------------------------------------------------------
    //
    // It must COINCIDE WITH THE ARTIFACT, which is the whole reason the previous four instruments
    // could each be true and useless. The reproducer is the user's own words: "schnell hintereinander
    // die Optionstaste drücken". So the trigger is the burst itself, plus the settle after it,
    // because the artifact PERSISTS ("bewegt sich mit dem Kopf mit") — by the time he has turned his
    // head to notice it, the burst is already over.
    //
    // WALL TIME, NOT FRAMES, AND UNSCALED. The pause menu this key opens can set Time.timeScale to 0,
    // which freezes Time.time; Time.unscaledTime keeps running. Frame counts would also make the same
    // window mean different things at 72 / 90 / 120 Hz.
    //
    // NOT A BEHAVIOUR TIMEOUT. The standing ruling "Ich will gar keine Zeitlimits dieser Art" is about
    // remedies that expire and hand something back. Nothing here changes what the mod does; these
    // numbers only decide WHEN a read-only census prints.

    /// <summary>Taps closer together than this chain into one burst. Human "rapid" tapping is 3-8 Hz;
    /// even a deliberate 1.5 Hz double tap chains at this width, so the window cannot split a burst
    /// the user would call "schnell hintereinander".</summary>
    private const float BurstChainSeconds = 1.5f;

    /// <summary>The tap of a chain that fires the immediate BURST census. Three, because two taps is
    /// an open-then-close the user does all the time without the rim, and because the reported
    /// reproducer is a repeated hammer rather than a single toggle.</summary>
    private const int BurstTapThreshold = 3;

    /// <summary>First settle census, seconds after the LAST tap of a chain. Long enough that the
    /// game's own Show/Hide cascade and the mod's one-frame reconcile have all landed, short enough
    /// that the frame still looks like the one he is complaining about.</summary>
    private const float SettleFirstSeconds = 0.75f;

    /// <summary>Second settle census. This is the "it is STILL there and the head has moved" reading —
    /// the artifact follows the head, so a rim that survives to here is a standing object, not a
    /// one-frame transition.</summary>
    private const float SettleSecondSeconds = 4.0f;

    /// <summary>Hard cap on census runs per session, so a hammered button cannot flood the log
    /// ([[a-quiet-log-silenced-the-backlog]] is about the opposite failure; this is the guard that
    /// lesson needs). One reproduction costs at most four: BASELINE, BURST and two SETTLED.</summary>
    private const int MaxRuns = 8;

    /// <summary>How many settle runs a chain that never reached <see cref="BurstTapThreshold"/> may
    /// spend. Without this, one idle tap at the main menu could eat two of the eight runs before the
    /// player ever tries to reproduce; with it, a genuine two-tap reproduction is still covered.</summary>
    private const int MaxSubThresholdSettles = 2;

    // ---- ROW CAPS ------------------------------------------------------------------------------

    private const int MaxCameraRows = 16;
    private const int MaxCanvasRows = 24;
    private const int MaxGraphicRows = 10;
    private const int MaxRendererRows = 10;
    private const int MaxHeadChildRows = 24;

    // ---- THE SHAPE -----------------------------------------------------------------------------
    //
    // Read off .planning/debug/linkes_element.png: the band's right edge sits at 467 px of a 2000 px
    // wide render = 0.23, its left edge IS the frame edge, and it spans the full height. The bounds
    // below are deliberately generous around that — the job of this test is to make a row say
    // "MATCHES THE PICTURE" or not, and a near miss silently rejected is a lost answer.

    /// <summary>A matching band starts at (or within a hair of) the left edge of the frame.</summary>
    private const float ShapeLeftEdgeMax = 0.08f;

    /// <summary>Narrowest right edge still called a band rather than a sliver.</summary>
    private const float ShapeRightEdgeMin = 0.10f;

    /// <summary>Widest right edge still called a band rather than half the frame. The picture says
    /// 0.23; this is twice that.</summary>
    private const float ShapeRightEdgeMax = 0.45f;

    /// <summary>A matching band reaches at least this low.</summary>
    private const float ShapeBottomMax = 0.12f;

    /// <summary>A matching band reaches at least this high.</summary>
    private const float ShapeTopMin = 0.88f;

    /// <summary>Effective alpha below which a graphic draws nothing worth naming. Deliberately LOWER
    /// than <c>CanvasConversion.FitMinAlpha</c> (0.05): the rim is translucent and the point of this
    /// pass is not to miss it.</summary>
    private const float MinVisibleAlpha = 0.02f;

    /// <summary>
    /// Necessary-condition prefilter for the renderer sweep. A bounding sphere of radius
    /// <c>extents.magnitude</c> at distance <c>d</c> subtends at most <c>2·asin(r/d)</c>, so it CANNOT
    /// span the frame height unless <c>r/d</c> reaches the sine of the half vertical FOV. This is that
    /// bound with a safety factor of two, which makes it a true necessary condition rather than a
    /// guess — and it removes almost every renderer in a forest scene before the eight-corner
    /// projection, which is what keeps the sweep affordable. The count it rejects is printed, so a
    /// reader can see how much of the population it removed.
    /// </summary>
    private const float AngularSizeSafety = 0.5f;

    // ---- TRIGGER STATE (written by Tick / NoteOptionsTap, never by the logger) ------------------

    private static int s_runs;
    private static int s_totalTaps;
    private static int s_chainTaps;
    private static float s_lastTapTime = float.NegativeInfinity;
    private static bool s_chainReachedThreshold;
    private static bool s_baselineDone;
    private static int s_subThresholdSettles;
    private static float s_settleFirstDue = float.PositiveInfinity;
    private static float s_settleSecondDue = float.PositiveInfinity;

    // ---- SCRATCH (touched only by the logger and its helpers) ----------------------------------

    private static readonly Vector3[] RectCorners = new Vector3[4];
    private static readonly Vector3[] BoundsCorners = new Vector3[8];
    private static readonly List<Graphic> GraphicScratch = new(64);
    private static readonly StringBuilder Sb = new(512);

    /// <summary>
    /// One options-key tap edge. Called from <see cref="OptionsToggle"/> on EVERY short-tap
    /// edge — including a press the toggle then refuses as "spent" — because a spent press is still a
    /// press the player made, and "schnell hintereinander gedrückt" is a statement about his thumb,
    /// not about which taps the mod chose to act on.
    /// </summary>
    internal static void NoteOptionsTap()
    {
        float now = Time.unscaledTime;
        bool chained = now - s_lastTapTime <= BurstChainSeconds;
        s_lastTapTime = now;
        s_totalTaps++;
        if (chained)
        {
            s_chainTaps++;
        }
        else
        {
            s_chainTaps = 1;
            s_chainReachedThreshold = false;
        }

        // Every tap re-arms both settle censuses, so a chain fires them once, AFTER the tapping
        // stops. That is the guarantee that a session which reproduces the bug produces a census:
        // reproducing it requires tapping, and any tapping at all arms a settled read.
        s_settleFirstDue = now + SettleFirstSeconds;
        s_settleSecondDue = now + SettleSecondSeconds;

        if (!s_baselineDone)
        {
            // The BASELINE is the diff partner. The artifact is a DIFFERENCE from normal, and a burst
            // census with nothing to compare it against makes every row look suspicious.
            s_baselineDone = true;
            Fire("BASELINE — the FIRST options tap of the session, before any burst. Read this row set as "
                 + "the clean picture and diff the later ones against it");
            return;
        }

        if (s_chainTaps >= BurstTapThreshold && !s_chainReachedThreshold)
        {
            s_chainReachedThreshold = true;
            Fire($"BURST — tap {s_chainTaps} of a chain of taps no more than {BurstChainSeconds:F1} s apart "
                 + $"({s_totalTaps} tap(s) this session). This is the user's reproducer: \"Wenn man schnell "
                 + "hintereinander die Optionstaste drückt\"");
        }
    }

    /// <summary>
    /// Per-frame pump, called from <see cref="OptionsToggle.Tick"/>. Two float compares and a
    /// return when nothing is armed — the sweep itself only ever runs from here on a DUE settle, or
    /// from <see cref="NoteOptionsTap"/> on a tap edge. It is never on a per-frame path.
    /// </summary>
    internal static void Tick()
    {
        if (float.IsPositiveInfinity(s_settleFirstDue) && float.IsPositiveInfinity(s_settleSecondDue))
            return;

        float now = Time.unscaledTime;
        if (now >= s_settleFirstDue)
        {
            s_settleFirstDue = float.PositiveInfinity;
            FireSettle(SettleFirstSeconds);
        }
        if (now >= s_settleSecondDue)
        {
            s_settleSecondDue = float.PositiveInfinity;
            FireSettle(SettleSecondSeconds);
        }
    }

    /// <summary>A settle census, budget-checked. See <see cref="MaxSubThresholdSettles"/>.</summary>
    private static void FireSettle(float delay)
    {
        if (!s_chainReachedThreshold)
        {
            if (s_subThresholdSettles >= MaxSubThresholdSettles)
                return;
            s_subThresholdSettles++;
        }
        Fire($"SETTLED — {delay:F2} s after the last of a {s_chainTaps}-tap chain "
             + $"({(s_chainReachedThreshold ? "reached" : "did NOT reach")} the {BurstTapThreshold}-tap burst "
             + "threshold). The rim PERSISTS and follows the head, so this read is the one taken while the "
             + "user would be looking at it");
    }

    /// <summary>
    /// Budget gate. Kept OUT of the logger so the logger writes no state anything else reads
    /// ([[a-write-inside-a-logger]]).
    ///
    /// <para>THE CATCH IS LOAD-BEARING. <see cref="NoteOptionsTap"/> is called from the middle of
    /// <see cref="OptionsToggle.Tick"/>, and although that whole Tick runs under
    /// <c>TickGuard.Run</c> (so a throw cannot amputate the rest of the WorldUI chain —
    /// [[a-thrown-listener-amputates-the-chain]]), a throw raised HERE would still abandon the rest
    /// of the tap handler and the menu would not open. That breaks the standing user ruling that the
    /// options menu must ALWAYS be openable, and it would break it in the exact burst the player is
    /// using to reproduce the bug. A diagnostic sweeping arbitrary game objects must not be able to
    /// cost him his menu, so the failure is reported at Error tier and the tap continues.</para>
    /// </summary>
    private static void Fire(string why)
    {
        if (s_runs >= MaxRuns)
            return;
        s_runs++;
        try
        {
            LogEyeReachCensus(why, s_runs);
        }
        catch (System.Exception ex)
        {
            VRLog.Error("WorldUI", $"EYE CENSUS THREW on run {s_runs}/{MaxRuns} ({why}) — the census is "
                + "read-only, so nothing was left half-applied, but this run produced no answer and the "
                + $"options tap that triggered it continues normally: {ex}");
        }
    }

    // ============================================================================================
    //  THE CENSUS
    // ============================================================================================

    /// <summary>
    /// Enumerate the scene and print what can paint into an eye. Read-only: it writes nothing but its
    /// own scratch buffers.
    /// </summary>
    private static void LogEyeReachCensus(string why, int run)
    {
        long t0 = Stopwatch.GetTimestamp();
        Camera? head = Rig.VRRigDriver.HeadCamera;
        int matches = 0;

        // ---- CAMERAS ---------------------------------------------------------------------------
        // FindObjectsOfType<Camera>(true), not Camera.GetAllCameras: the latter returns only enabled
        // cameras, and a camera that is disabled RIGHT NOW is still worth naming when the question is
        // "what could have painted this".
        Camera[] cameras = Object.FindObjectsOfType<Camera>(true);
        int camRows = 0;
        int camPaints = 0;
        var camLines = new List<string>(cameras.Length);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null)
                continue;
            bool live = cam.isActiveAndEnabled;
            string target = cam.targetTexture != null ? $"'{cam.targetTexture.name}'" : "BACKBUFFER (null)";
            string reach = !live
                ? "no — the camera is not active+enabled, so it renders nothing"
                : cam.targetTexture != null
                    ? $"no — it renders into {target}, which is not a display"
                    : cam.stereoTargetEye == StereoTargetEyeMask.None
                        ? "DESKTOP ONLY — it reaches the backbuffer but not the HMD. It can still be what the "
                          + "MONITOR shows if the runtime ignored XRSettings.gameViewRenderMode (see the ITEM9 "
                          + "readback line)"
                        : $"YES — backbuffer, stereo={cam.stereoTargetEye}: it composites into the eye texture(s)";
            Rect r = cam.rect;
            string shape = ShapeVerdict(r, out bool camShape);
            if (live && cam.targetTexture == null)
                camPaints++;
            if (camShape)
                matches++;
            if (camRows < MaxCameraRows)
            {
                camRows++;
                camLines.Add($"EYE CENSUS CAM '{cam.name}' path={ScenePath(cam.transform)} "
                    + $"active={cam.gameObject.activeInHierarchy} enabled={cam.enabled} depth={cam.depth:F1} "
                    + $"clear={cam.clearFlags} bg={Rgba(cam.backgroundColor)} target={target} "
                    + $"mask=0x{cam.cullingMask:X8} stereo={cam.stereoTargetEye} "
                    + $"rect=({r.x:F3},{r.y:F3},{r.width:F3},{r.height:F3})"
                    + $"{(head != null && cam == head ? " [VR HEAD]" : "")}"
                    + $"{(IsModOwned(cam.transform) ? " [MOD]" : " [GAME]")} "
                    + $"→ PAINTS INTO AN EYE: {reach}. ITS OWN rect MATCHES THE PICTURE: {shape}");
            }
        }

        // ---- HEADER (printed first, so the rows below always have their context) ----------------
        // HW-VERIFY: the run header. If a burst is reported and no EYE CENSUS RUN line exists, the
        // trigger never fired and nothing below it means anything — check this before anything else.
        VRLog.Note("WorldUI", $"EYE CENSUS RUN {run}/{MaxRuns} — {why}. HEAD CAMERA: "
            + (head != null
                ? $"'{head.name}' fov={head.fieldOfView:F1} stereoEnabled={head.stereoEnabled} "
                  + $"mask=0x{head.cullingMask:X8} rect=({head.rect.x:F3},{head.rect.y:F3},{head.rect.width:F3},"
                  + $"{head.rect.height:F3}) clear={head.clearFlags} bg={Rgba(head.backgroundColor)}"
                : "NONE — VRRigDriver.HeadCamera is null, so the renderer section below cannot run and no "
                  + "viewport rect can be computed")
            + $". SCREEN {Screen.width}x{Screen.height}, XR eye texture "
            + $"{UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight}, "
            + $"XR mirror mode={UnityEngine.XR.XRSettings.gameViewRenderMode}, "
            + $"mode={VRModeStateMachine.CurrentMode}, room={VRModeStateMachine.TableInFrontOfPlayer}, "
            + $"flat screen {(FlatScreen.ScreenVisible ? "SHOWN" : "HIDDEN")}. THE PICTURE THIS IS RANKED "
            + "AGAINST: a full-height, left-anchored, translucent dark band whose right edge sits at ~0.23 "
            + "of the frame width, in ONE eye.");

        for (int i = 0; i < camLines.Count; i++)
            VRLog.Note("WorldUI", camLines[i]);
        if (cameras.Length > camRows)
        {
            VRLog.Note("WorldUI", $"EYE CENSUS CAM +{cameras.Length - camRows} further camera(s) NOT NAMED "
                + $"(row cap {MaxCameraRows}).");
        }

        // ---- CANVASES + GRAPHICS ----------------------------------------------------------------
        Canvas[] canvases = Object.FindObjectsOfType<Canvas>(true);
        int canvasRows = 0;
        int quietCanvases = 0;
        int graphicsExamined = 0;
        int graphicRows = 0;
        int tallGraphics = 0;
        var graphicLines = new List<string>(MaxGraphicRows);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas c = canvases[i];
            if (c == null)
                continue;
            Canvas root = c.rootCanvas != null ? c.rootCanvas : c;
            bool overlay = root.renderMode == RenderMode.ScreenSpaceOverlay
                           || (root.renderMode == RenderMode.ScreenSpaceCamera && root.worldCamera == null);
            Camera? drawer = root.renderMode == RenderMode.WorldSpace ? head : root.worldCamera;
            bool live = c.enabled && c.gameObject.activeInHierarchy;
            bool cannotReach;
            string reach;
            if (overlay)
            {
                cannotReach = false;
                reach = "YES — OVERLAY: no camera draws it; Unity composites it onto the target display after "
                        + "the camera loop, so it is head-locked and under MultiPass lands in ONE eye texture. "
                        + "No layer and no culling mask can stop it";
            }
            else if (root.renderMode == RenderMode.WorldSpace)
            {
                if (head == null)
                {
                    cannotReach = false;
                    reach = "unknown — WORLD SPACE but there is no head camera to test the mask against";
                }
                else if ((head.cullingMask & (1 << c.gameObject.layer)) != 0)
                {
                    cannotReach = false;
                    reach = $"YES — WORLD SPACE on layer {c.gameObject.layer}, which the head camera's mask "
                            + $"0x{head.cullingMask:X8} CONTAINS";
                }
                else
                {
                    cannotReach = true;
                    reach = $"no — WORLD SPACE on layer {c.gameObject.layer}, excluded by the head camera's mask "
                            + $"0x{head.cullingMask:X8}";
                }
            }
            else if (drawer != null && drawer.targetTexture == null && drawer.isActiveAndEnabled)
            {
                cannotReach = false;
                reach = $"YES — drawn by '{drawer.name}', which renders to the BACKBUFFER";
            }
            else
            {
                cannotReach = true;
                reach = $"no — drawn by '{(drawer != null ? drawer.name : "<none>")}', which renders into "
                        + $"'{(drawer != null && drawer.targetTexture != null ? drawer.targetTexture.name : "<none>")}'";
            }

            // The rect the canvas itself occupies. Engine truth for the alpha
            // (CanvasRenderer.GetInheritedAlpha) rather than a modelled CanvasGroup chain —
            // [[inherited-alpha-is-not-the-group]].
            Rect? canvasRect = c.transform is RectTransform crt
                ? NormalizedRectOf(crt, root, drawer, overlay, head, out _)
                : null;
            string canvasShape = canvasRect.HasValue
                ? ShapeVerdict(canvasRect.Value, out _)
                : "unknown (no rect could be projected)";
            bool shapeHit = canvasRect.HasValue && ShapeMatches(canvasRect.Value);
            if (shapeHit)
                matches++;

            float brightest = 0f;
            if (c.isRootCanvas && live)
            {
                GraphicScratch.Clear();
                c.GetComponentsInChildren(true, GraphicScratch);
                for (int g = 0; g < GraphicScratch.Count; g++)
                {
                    Graphic gr = GraphicScratch[g];
                    if (gr == null || !gr.enabled || !gr.gameObject.activeInHierarchy)
                        continue;
                    Canvas? own = gr.canvas;
                    if (own == null || !own.enabled)
                        continue;
                    graphicsExamined++;
                    float a = gr.color.a * gr.canvasRenderer.GetInheritedAlpha();
                    if (a > brightest)
                        brightest = a;
                    if (a < MinVisibleAlpha)
                        continue;
                    Canvas ownRoot = own.rootCanvas != null ? own.rootCanvas : own;
                    bool ownOverlay = ownRoot.renderMode == RenderMode.ScreenSpaceOverlay
                                      || (ownRoot.renderMode == RenderMode.ScreenSpaceCamera
                                          && ownRoot.worldCamera == null);
                    Camera? ownDrawer = ownRoot.renderMode == RenderMode.WorldSpace ? head : ownRoot.worldCamera;
                    Rect? gRect = NormalizedRectOf(gr.rectTransform, own, ownDrawer, ownOverlay, head,
                        out string frame);
                    if (!gRect.HasValue)
                        continue;
                    Rect gv = gRect.Value;
                    if (gv.yMin <= ShapeBottomMax && gv.yMax >= ShapeTopMin)
                        tallGraphics++;
                    if (!ShapeMatches(gv))
                        continue;
                    matches++;
                    if (graphicRows >= MaxGraphicRows)
                        continue;
                    graphicRows++;
                    graphicLines.Add($"EYE CENSUS GRAPHIC MATCH '{gr.name}' type={gr.GetType().Name} "
                        + $"path={ScenePath(gr.transform)} canvas='{own.name}' mode={ownRoot.renderMode} "
                        + $"layer={gr.gameObject.layer} colour={Rgba(gr.color)} effectiveAlpha={a:F3} "
                        + $"raycastTarget={gr.raycastTarget} "
                        + $"material='{(gr.material != null ? gr.material.name : "<none>")}' "
                        + $"rect=({gv.xMin:F3},{gv.yMin:F3})-({gv.xMax:F3},{gv.yMax:F3}) in {frame}"
                        + $"{(IsModOwned(gr.transform) ? " [MOD]" : " [GAME]")} "
                        + $"→ MATCHES THE PICTURE: {ShapeVerdict(gv, out _)}");
                }
                GraphicScratch.Clear();
            }

            bool couldPaint = live && !cannotReach && (brightest >= MinVisibleAlpha || !c.isRootCanvas);
            if ((!couldPaint && !shapeHit) || canvasRows >= MaxCanvasRows)
            {
                quietCanvases++;
                continue;
            }
            canvasRows++;
            VRLog.Note("WorldUI", $"EYE CENSUS CANVAS '{c.name}'{(c.isRootCanvas ? " ROOT" : " nested")} "
                + $"path={ScenePath(c.transform)} mode={root.renderMode} enabled={c.enabled} "
                + $"active={c.gameObject.activeInHierarchy} cam='{(drawer != null ? drawer.name : "<none>")}' "
                + $"camTarget='{(drawer != null && drawer.targetTexture != null ? drawer.targetTexture.name : "backbuffer/none")}' "
                + $"order={c.sortingOrder} sortingLayer='{c.sortingLayerName}' override={c.overrideSorting} "
                + $"layer={c.gameObject.layer} brightestGraphicAlpha={brightest:F3} "
                + $"rect={(canvasRect.HasValue ? $"({canvasRect.Value.xMin:F3},{canvasRect.Value.yMin:F3})-({canvasRect.Value.xMax:F3},{canvasRect.Value.yMax:F3})" : "<not projectable>")}"
                + $"{(IsModOwned(c.transform) ? " [MOD]" : " [GAME]")} "
                + $"→ REACHES AN EYE: {reach}. ITS RECT MATCHES THE PICTURE: {canvasShape}");
        }
        VRLog.Note("WorldUI", $"EYE CENSUS CANVAS SUMMARY {canvases.Length} canvas(es) in the scene, "
            + $"{canvasRows} named above, {quietCanvases} not named (disabled, inactive, every graphic below "
            + $"alpha {MinVisibleAlpha:F2}, drawn into a RenderTexture, culled by layer, or past the row cap "
            + $"of {MaxCanvasRows}). GRAPHICS: {graphicsExamined} examined under the painting root canvases, "
            + $"{tallGraphics} of them full-height, {graphicRows} MATCHING the picture.");
        for (int i = 0; i < graphicLines.Count; i++)
        {
            // HW-VERIFY: a UI band. A row here names the Image that IS the rim, with its scene path.
            VRLog.Note("WorldUI", graphicLines[i]);
        }

        // ---- RENDERERS ---------------------------------------------------------------------------
        int rendererRows = 0;
        int renderersExamined = 0;
        if (head == null)
        {
            VRLog.Note("WorldUI", "EYE CENSUS RENDERER SUMMARY skipped — VRRigDriver.HeadCamera is null, so "
                + "nothing can be projected into an eye's viewport. That is itself a finding if VR is running.");
        }
        else
        {
            Vector3 camPos = head.transform.position;
            float minRatio = AngularSizeSafety * Mathf.Sin(Mathf.Deg2Rad * head.fieldOfView * 0.5f);
            Renderer[] renderers = Object.FindObjectsOfType<Renderer>();
            int rejectedByMask = 0;
            int rejectedByAngularSize = 0;
            int tallRenderers = 0;
            var rendererLines = new List<string>(MaxRendererRows);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled)
                    continue;
                renderersExamined++;
                if ((head.cullingMask & (1 << r.gameObject.layer)) == 0)
                {
                    rejectedByMask++;
                    continue;
                }
                Bounds b = r.bounds;
                float ext = b.extents.magnitude;
                float dist = Vector3.Distance(camPos, b.center);
                if (dist > ext && ext < minRatio * dist)
                {
                    rejectedByAngularSize++;
                    continue;
                }
                FillBoundsCorners(b);
                if (!ViewportRectOf(head, BoundsCorners, 8, Camera.MonoOrStereoscopicEye.Left,
                        out Rect left, out int behind))
                    continue;
                if (left.yMin <= ShapeBottomMax && left.yMax >= ShapeTopMin)
                    tallRenderers++;
                if (!ShapeMatches(left))
                    continue;
                matches++;
                if (rendererRows >= MaxRendererRows)
                    continue;
                rendererRows++;
                string rightRect = head.stereoEnabled
                        && ViewportRectOf(head, BoundsCorners, 8, Camera.MonoOrStereoscopicEye.Right,
                            out Rect right, out _)
                    ? $"({right.xMin:F3},{right.yMin:F3})-({right.xMax:F3},{right.yMax:F3})"
                    : "<not stereo / not projectable>";
                Material? mat = r.sharedMaterial;
                rendererLines.Add($"EYE CENSUS RENDERER MATCH '{r.name}' type={r.GetType().Name} "
                    + $"path={ScenePath(r.transform)} layer={r.gameObject.layer} isVisible={r.isVisible} "
                    + $"shadows={r.shadowCastingMode} material='{(mat != null ? mat.name : "<none>")}' "
                    + $"shader='{(mat != null && mat.shader != null ? mat.shader.name : "<none>")}' "
                    + $"queue={(mat != null ? mat.renderQueue : -1)} boundsCentre={b.center.ToString("F3")} "
                    + $"extents={b.extents.ToString("F3")} distToHead={dist:F3} cornersBehind={behind}"
                    + $"{(IsModOwned(r.transform) ? " [MOD]" : " [GAME]")} "
                    + $"LEFT EYE rect=({left.xMin:F3},{left.yMin:F3})-({left.xMax:F3},{left.yMax:F3}) "
                    + $"RIGHT EYE rect={rightRect} → MATCHES THE PICTURE: {ShapeVerdict(left, out _)}");
            }
            for (int i = 0; i < rendererLines.Count; i++)
            {
                // HW-VERIFY: a 3D band. A row here names an object the head camera draws whose projected
                // bounds ARE the photographed rim, and prints its LEFT and RIGHT eye rects separately —
                // the direct test of the user's "aus dem linken augenwinkel".
                VRLog.Note("WorldUI", rendererLines[i]);
            }
            VRLog.Note("WorldUI", $"EYE CENSUS RENDERER SUMMARY {renderers.Length} active renderer(s), "
                + $"{renderersExamined} enabled, {rejectedByMask} excluded by the head camera's culling mask "
                + $"0x{head.cullingMask:X8}, {rejectedByAngularSize} rejected by the angular-size prefilter "
                + "(their bounding sphere cannot subtend the frame height from here — a necessary condition, "
                + $"safety factor {AngularSizeSafety:F1}), {tallRenderers} full-height, {rendererRows} MATCHING "
                + "the picture.");

            // What is glued to the head. Anything that follows the head is very likely parented here, and
            // this is the cheapest place to see it whole.
            Sb.Clear();
            Transform ht = head.transform;
            int shown = 0;
            for (int i = 0; i < ht.childCount && shown < MaxHeadChildRows; i++)
            {
                Transform ch = ht.GetChild(i);
                shown++;
                Sb.Append(" | '").Append(ch.name).Append("' active=").Append(ch.gameObject.activeInHierarchy)
                    .Append(" layer=").Append(ch.gameObject.layer)
                    .Append(" localPos=").Append(ch.localPosition.ToString("F3"))
                    .Append(" localScale=").Append(ch.localScale.ToString("F3"))
                    .Append(ch.GetComponent<Renderer>() != null ? " HAS-RENDERER" : string.Empty)
                    .Append(ch.GetComponent<Canvas>() != null ? " HAS-CANVAS" : string.Empty)
                    .Append(ch.GetComponent<Camera>() != null ? " HAS-CAMERA" : string.Empty);
            }
            VRLog.Note("WorldUI", $"EYE CENSUS HEADCHILD {ht.childCount} direct child(ren) of '{head.name}'"
                + (ht.childCount > shown ? $" ({shown} named, row cap {MaxHeadChildRows})" : string.Empty)
                + (Sb.Length == 0 ? ": none." : ":" + Sb));
            Sb.Clear();
        }

        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        // HW-VERIFY: THE VERDICT. This is the line the next hardware round is decided on. If it says 0
        // rows matched while the user reports the rim on screen, then the rim is drawn by something NONE
        // of the four sections above can see, and the blind spots named on this line are the entire
        // remaining search space.
        VRLog.Note("WorldUI", $"EYE CENSUS VERDICT run {run}/{MaxRuns}: {matches} row(s) MATCH the "
            + $"photographed shape — a full-height, left-anchored band with its right edge between "
            + $"{ShapeRightEdgeMin:F2} and {ShapeRightEdgeMax:F2} of the frame. {camPaints} camera(s) paint to "
            + $"a display, {cameras.Length} camera(s), {canvases.Length} canvas(es), {graphicsExamined} "
            + $"graphic(s) and {renderersExamined} enabled renderer(s) were examined. COST: {ms:F1} ms "
            + $"measured, {run}/{MaxRuns} run(s) spent this session, {s_totalTaps} options tap(s) so far. "
            + "STILL INVISIBLE TO THIS CENSUS, in falling order of likelihood: a full-screen image effect "
            + "(OnRenderImage or a CommandBuffer on any camera), an XR compositor overlay layer submitted "
            + "outside Unity's camera loop, an IMGUI OnGUI draw, geometry displaced by a vertex shader "
            + "outside its own bounds ([[culling-cannot-see-vertex-shaders]]), and any renderer removed by "
            + "the angular-size prefilter above.");
    }

    // ============================================================================================
    //  HELPERS — all read-only
    // ============================================================================================

    /// <summary>Does this rect describe the band the user photographed?</summary>
    private static bool ShapeMatches(Rect r) =>
        r.xMin <= ShapeLeftEdgeMax
        && r.xMax >= ShapeRightEdgeMin && r.xMax <= ShapeRightEdgeMax
        && r.yMin <= ShapeBottomMax && r.yMax >= ShapeTopMin;

    /// <summary>
    /// The per-row verdict, in words. A census of forty rows with no verdict is another wall of text
    /// ([[one-contributor-is-not-the-union]]), so every row says whether it could be the picture and —
    /// when it could not — WHICH term failed.
    /// </summary>
    private static string ShapeVerdict(Rect r, out bool match)
    {
        bool left = r.xMin <= ShapeLeftEdgeMax;
        bool band = r.xMax >= ShapeRightEdgeMin && r.xMax <= ShapeRightEdgeMax;
        bool tall = r.yMin <= ShapeBottomMax && r.yMax >= ShapeTopMin;
        match = left && band && tall;
        if (match)
        {
            return $"YES — left-anchored (starts at x={r.xMin:F3}), right edge at x={r.xMax:F3}, spans the full "
                   + $"height (y {r.yMin:F3}..{r.yMax:F3}). The photograph says the right edge is at 0.23.";
        }
        var sb = new StringBuilder(128);
        sb.Append("no (");
        if (!left)
            sb.Append($"not left-anchored: starts at x={r.xMin:F3}; ");
        if (!band)
            sb.Append($"right edge x={r.xMax:F3} is outside {ShapeRightEdgeMin:F2}..{ShapeRightEdgeMax:F2}; ");
        if (!tall)
            sb.Append($"not full height: y {r.yMin:F3}..{r.yMax:F3}; ");
        sb.Length -= 2;
        sb.Append(')');
        return sb.ToString();
    }

    /// <summary>
    /// A RectTransform's rect in 0..1 frame coordinates, or null when nothing about it projects in
    /// front of the drawing camera. For an OVERLAY canvas <c>GetWorldCorners</c> already returns SCREEN
    /// PIXELS, so those are normalised against the canvas's own <c>pixelRect</c>; everything else is
    /// projected through the camera that actually draws it, in the LEFT eye — the eye the desktop
    /// mirror shows and the eye the user is describing.
    /// </summary>
    private static Rect? NormalizedRectOf(RectTransform rect, Canvas canvas, Camera? drawer, bool overlay,
        Camera? head, out string frame)
    {
        rect.GetWorldCorners(RectCorners);
        Canvas root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
        if (overlay)
        {
            Rect pr = root.pixelRect;
            float w = pr.width > 1f ? pr.width : Mathf.Max(Screen.width, 1);
            float h = pr.height > 1f ? pr.height : Mathf.Max(Screen.height, 1);
            frame = $"overlay screen px (canvas pixelRect {pr.width:F0}x{pr.height:F0})";
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float x = (RectCorners[i].x - pr.x) / w;
                float y = (RectCorners[i].y - pr.y) / h;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        Camera? cam = drawer != null ? drawer : head;
        if (cam == null)
        {
            frame = "<no camera to project through>";
            return null;
        }
        frame = $"viewport of '{cam.name}'";
        return ViewportRectOf(cam, RectCorners, 4, Camera.MonoOrStereoscopicEye.Left, out Rect vr, out _)
            ? vr
            : null;
    }

    /// <summary>
    /// The viewport-space bounding rect of the first <paramref name="count"/> of
    /// <paramref name="pts"/>, for one eye. Points BEHIND the camera are dropped and COUNTED rather
    /// than folded in, because a projected point behind the near plane mirrors and would silently
    /// invent a rect ([[one-step-too-early]]). Returns false when nothing at all is in front.
    /// </summary>
    private static bool ViewportRectOf(Camera cam, Vector3[] pts, int count,
        Camera.MonoOrStereoscopicEye eye, out Rect rect, out int behind)
    {
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        int used = 0;
        behind = 0;
        for (int i = 0; i < count; i++)
        {
            Vector3 v = cam.stereoEnabled ? cam.WorldToViewportPoint(pts[i], eye) : cam.WorldToViewportPoint(pts[i]);
            if (v.z <= 0f)
            {
                behind++;
                continue;
            }
            used++;
            if (v.x < x0) x0 = v.x;
            if (v.x > x1) x1 = v.x;
            if (v.y < y0) y0 = v.y;
            if (v.y > y1) y1 = v.y;
        }
        if (used == 0)
        {
            rect = default;
            return false;
        }
        rect = Rect.MinMaxRect(x0, y0, x1, y1);
        return true;
    }

    /// <summary>The eight corners of <paramref name="b"/>, into the shared scratch.</summary>
    private static void FillBoundsCorners(Bounds b)
    {
        Vector3 c = b.center, e = b.extents;
        BoundsCorners[0] = new Vector3(c.x - e.x, c.y - e.y, c.z - e.z);
        BoundsCorners[1] = new Vector3(c.x + e.x, c.y - e.y, c.z - e.z);
        BoundsCorners[2] = new Vector3(c.x - e.x, c.y + e.y, c.z - e.z);
        BoundsCorners[3] = new Vector3(c.x + e.x, c.y + e.y, c.z - e.z);
        BoundsCorners[4] = new Vector3(c.x - e.x, c.y - e.y, c.z + e.z);
        BoundsCorners[5] = new Vector3(c.x + e.x, c.y - e.y, c.z + e.z);
        BoundsCorners[6] = new Vector3(c.x - e.x, c.y + e.y, c.z + e.z);
        BoundsCorners[7] = new Vector3(c.x + e.x, c.y + e.y, c.z + e.z);
    }

    /// <summary>Hierarchy path, root-first, depth-capped. Not
    /// <c>MainMenuLogoPlacement.PathOf</c>: that one walks to the root uncapped, and a deep uGUI
    /// subtree would put a hundred names on a line whose whole job is to be readable.</summary>
    private static string ScenePath(Transform t)
    {
        const int MaxDepth = 14;
        string path = t.name;
        Transform? p = t.parent;
        for (int i = 0; p != null && i < MaxDepth; p = p.parent, i++)
            path = p.name + "/" + path;
        return p != null ? ".../" + path : path;
    }

    /// <summary>
    /// Is this object inside a mod-owned subtree? The house convention is the GameObject NAME prefix
    /// <c>"GloomhavenVR."</c>, which every mod-created root uses. The existing predicates
    /// (<c>PanelSupersample.IsModOwned</c>, <c>CameraOrderProbe.IsOurs</c>, …) all test one object's
    /// own name; this one walks the ancestors, because a mod-owned band could be a plainly named child
    /// under a mod host ([[containment-is-not-identity]] cuts the other way for a census: here the
    /// question really is "whose subtree is this in").
    /// </summary>
    private static bool IsModOwned(Transform t)
    {
        const int MaxDepth = 16;
        Transform? p = t;
        for (int i = 0; p != null && i < MaxDepth; p = p.parent, i++)
        {
            if (p.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Colour as 0..1 RGBA, so a "translucent dark grey" is readable as one.</summary>
    private static string Rgba(Color c) => $"({c.r:F3},{c.g:F3},{c.b:F3},a={c.a:F3})";
}
