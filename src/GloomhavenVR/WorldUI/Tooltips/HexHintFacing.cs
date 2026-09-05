using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Per-frame head-follow for the hover HEX-HINT panels (task #4 / #7).
///
/// When the laser hovers a board field the game pops a hint/explanation label — e.g.
/// "Geschlossene Tür" (closed door), chests, obstacles, pressure plates, portals,
/// terrain — via <c>UITextInfoPanel</c>, and carryable-quest-item cards via
/// <c>UIPropInfoPanel</c>. Both are hover-driven info popups (verified:
/// <c>decompiled/GH.Runtime/WorldspaceStarHexDisplay.cs</c> <c>ShowTooltipForTile()</c>
/// routes the door label to <c>UITextInfoPanel.Show</c> at :3607 with the
/// <c>CLOSED_DOOR_TOOLTIP</c> string at :3441). <see cref="GloomhavenVR.WorldUI.Surfaces.PropInfoSurface"/> already
/// converts them to world space and docks them at the fixed <see cref="PanelSlot.PropInfo"/>
/// pose — but that slot rotation faces the CACHED SEAT yaw (PanelLayout world-anchoring)
/// and its position sits low at the table edge, so after a snap-turn / world-grab / the
/// player simply leaning the hint no longer squarely faces the head and reads off to the
/// side. That is the reported "hint not facing the player".
///
/// This step runs in LateUpdate, AFTER PropInfoSurface's Update-time placement, and
/// OVERRIDES the host POSITION and ROTATION (PropInfoSurface keeps owning scale) — but
/// ONLY while the panel is actually visible (<see cref="UIWindow.IsVisible"/>). While
/// shown it drifts the hint to a comfortable reading spot near the CENTER of the player's
/// forward field of view (slightly below the gaze center, at a fixed distance × diorama
/// scale) with LAZY, critically-damped motion — <see cref="Vector3.SmoothDamp"/> for
/// position and a frame-rate-independent exponential slerp for rotation — so it eases
/// toward the center and settles rather than rigidly locking or hard-billboarding. The
/// lazy follow is gated by <see cref="WorldUIConfig.HexHintFollowView"/> (default on);
/// with it off we keep PropInfoSurface's dock position and only re-face the host to the
/// head. When the hint hides we do nothing to the host: the panel keeps PropInfoSurface's pose
/// and then releases.
///
/// <para><b>A HIDE IS NOT THE END OF AN EPISODE (ModBuild 448).</b> "The next hover starts fresh"
/// used to mean "every re-show starts fresh", because the drift was re-seeded from
/// <c>host.position</c> whenever the previous frame had not driven it. That reads as a neutral
/// starting point and is not one: it is the fixed <see cref="PanelSlot.PropInfo"/> dock that
/// <see cref="GloomhavenVR.WorldUI.Surfaces.PropInfoSurface"/> re-writes in Update on every
/// converted frame. The hover producers hide and re-show these windows several times a second
/// while the pointer rests on ONE prop — <c>UIPropInfoPanel.Hide</c>/<c>ShowTrap</c> from
/// <c>HoverRegisterer</c>'s enter/exit pair — so the re-seed fired continuously and pinned the
/// card at the dock, twitching. That is the 2026-09-05 report "Das Tooltip von einer neuen
/// gespawnten Falle zuckt und ist an der falschen Stelle": both halves, one line.
/// <see cref="FollowResumeGraceSeconds"/> now carries the drift across a hide, so a re-show
/// resumes where the card was; only a genuine absence re-seeds. The CHURN itself is a separate
/// defect with its own fix — see <c>Board.Patches.HoverPickPatch.TryPickHoverTarget</c> — and
/// this grace is what makes the card immune to it rather than merely quieter.</para>
///
/// <para><b>WHAT IT MUST NOT DO, since ModBuild 360.</b> User, 2026-09-03, verbatim: "Die Info bei
/// den props in der Hand folgt aktuell dem Kopf - das soll nicht sein ... Die Info die dem Kopf
/// folgt ist nur beim Laser-hover." The head-follow above is CORRECT and is left exactly as it is
/// for the laser hover, which is the case it was built for. But <c>UITextInfoPanel</c> is a
/// SHARED window: <c>GrabbableProp.PushInfo</c> raises the same singleton for a prop carried in the
/// hand, and this step gates only on <see cref="UIWindow.IsVisible"/>, so it could not tell "the
/// laser is hovering a hex" from "a prop is in the hand" and drove the held card to the centre of
/// view. The distinguishing term does not exist here and cannot be invented here - it lives in
/// <see cref="GloomhavenVR.WorldUI.Surfaces.PropInfoSurface.TryGetHeldDockPose"/>, which owns the
/// held-prop concept. While that returns true this step STANDS DOWN for the text panel and instead
/// re-asserts the held dock pose, so the LAST writer of the frame is still the prop-anchored one
/// (LateUpdate, after the hands have moved - the card cannot lag the hand by a frame). Every other
/// case, and the <c>UIPropInfoPanel</c> quest-item hint in every case, keeps the head-follow
/// untouched.</para>
///
/// Non-invasive: it reads the shared <see cref="CanvasConversion.ActivePanels"/> registry
/// to find each singleton panel's converted host by <see cref="ConvertedPanel.Target"/>
/// identity — it never touches <see cref="GloomhavenVR.WorldUI.Surfaces.PropInfoSurface"/>. If <c>PropInfoCards</c> is
/// off (panel never converted) the host lookup misses and this is a no-op. Facing
/// convention matches <c>PanelPlacement.Facing</c>: uGUI fronts render toward the viewer,
/// so the host's +Z points AWAY from the head. Occlusion-sane: only the transform is
/// touched (no sorting/material draw-through), and the host stays non-pokeable.
/// </summary>
internal sealed class HexHintFacing
{
    // READING SPOT — user-tunable since 2026-08-03 ("auch die Position von Hints beim
    // drüberhovern möchte ich in der Lage sein anzupassen"). The three numbers below used to be
    // the constants 0.6 / 0.12 / 0 and are now [WorldUI] HexHintDistance / HexHintDrop /
    // HexHintSide, read LIVE every frame so a settings stepper moves the hint while it is on
    // screen. The shipped defaults reproduce the old constants exactly.
    private static float FollowDistance => WorldUIConfig.HexHintDistance != null
        ? WorldUIConfig.HexHintDistance.Value : 0.6f;
    private static float FollowDrop => WorldUIConfig.HexHintDrop != null
        ? WorldUIConfig.HexHintDrop.Value : 0.12f;
    private static float FollowSide => WorldUIConfig.HexHintSide != null
        ? WorldUIConfig.HexHintSide.Value : 0f;

    /// <summary>SmoothDamp time constant for the lazy position drift (seconds) — bigger = lazier.</summary>
    private const float FollowSmoothTime = 0.28f;

    /// <summary>Exponential approach rate for the lazy rotation (per second) — bigger = snappier.</summary>
    private const float FollowRotLambda = 7f;

    /// <summary>
    /// RESUME GRACE (ModBuild 448). How long the lazy drift survives the hint going invisible
    /// before the next show is treated as a NEW episode and re-seeded from the dock.
    ///
    /// <para><b>Why the re-seed had to stop being unconditional.</b> The drift used to restart
    /// from <c>host.position</c> on every single re-show, with the comment "so it eases toward
    /// center rather than snapping" — correct for a first show, and wrong for a re-show a
    /// handful of frames later, because <c>host.position</c> is not a neutral starting point: it
    /// is the fixed <see cref="PanelSlot.PropInfo"/> dock that
    /// <see cref="GloomhavenVR.WorldUI.Surfaces.PropInfoSurface.PlaceWatch"/> re-writes in
    /// Update EVERY frame the panel is converted. So a re-seed is a teleport back to the dock,
    /// and a stream of them pins the card AT the dock while the accumulated damping twitches it:
    /// the user's two symptoms, "zuckt" and "an der falschen Stelle", from one line. The
    /// 2026-09-05 host log measures it: 62 of the session's 62 <c>PropInfoPanel</c>
    /// re-engagements fall inside the single trap-hover window at lines 142755-144104, some of
    /// them four log lines apart, each one interleaved with a 'Prop Info Panel' hidden/SHOWN
    /// pair. A 0.28 s SmoothDamp restarted every few frames never travels more than a few
    /// percent of the way to the reading spot.</para>
    ///
    /// <para><b>0.5 s, and why that number.</b> It is the placement latch the user already asked
    /// for and already has, verbatim, on the board-owned tooltip — <c>WorldTooltips</c>'s
    /// <c>HoverGraceSeconds</c> (user request #7b): "keep the canvas parked at the anchor this
    /// long so a micro-jitter off a tiny target does not teleport it". Same defect, same ruling,
    /// same window; this is that mechanism applied to the hint that had never been given it, not
    /// a second one beside it. It must also outlast
    /// <c>PropInfoSurface.ReleaseDelaySeconds</c> (0.3 s), so a hide that goes all the way to a
    /// release and re-conversion still resumes on the fresh host instead of re-seeding. IF YOU
    /// RETUNE EITHER, READ THE OTHER: they are one user ruling expressed twice.</para>
    /// </summary>
    private const float FollowResumeGraceSeconds = 0.5f;

    /// <summary>
    /// Routine episode reports per panel per session. A first-N budget alone would have burned
    /// itself on the session's first three door hovers and printed nothing for the trap the user
    /// is actually reporting — the same "a WORST field is the tail" trap the board tooltip's own
    /// 3-report cap fell into in the 2026-09-05 log. So <see cref="FlushEpisode"/> ALSO reports
    /// any episode whose largest one-frame move beats every episode already reported, up to
    /// <see cref="EpisodeReportHardCap"/> lines: the worst episode of the session is always in
    /// the log, whenever it happens.
    /// </summary>
    private const int EpisodeReportBudget = 3;

    /// <summary>Absolute ceiling on episode reports per panel per session (routine + new-worst).</summary>
    private const int EpisodeReportHardCap = 8;

    /// <summary>Per-panel follow state (singleton + damping accumulators).</summary>
    private sealed class HintState
    {
        public Component? Attached;
        public UIWindow? Window;
        public bool Engaged;
        public Vector3 Pos;
        public Vector3 PosVel;
        public Quaternion Rot;

        /// <summary>True for the <c>UITextInfoPanel</c> state, false for the
        /// <c>UIPropInfoPanel</c> one. Since ModBuild 366 this is the WINDOW discriminator only:
        /// which of the two a held prop's card is in is answered by
        /// <c>Board.FigureGrab.HeldPropCard.Owns</c>, because a trap in the hand now raises the
        /// rich window.</summary>
        public bool IsTextInfo;

        /// <summary>Edge state for the one-shot stand-down line (see <see cref="LateTick"/>).</summary>
        public bool StoodDown;

        // ---- resume grace (ModBuild 448) ------------------------------------------------------
        /// <summary><see cref="Pos"/>/<see cref="Rot"/>/<see cref="PosVel"/> carry a live drift.
        /// Separate from <see cref="Engaged"/>, which means "we drove the host LAST frame": a
        /// hide is allowed to end the second without ending the first.</summary>
        public bool DriftValid;

        /// <summary>Unscaled time of the last frame this step drove the host.</summary>
        public float LastDriveTime;

        /// <summary>True when the previous tick did not drive (used to count the hide/show churn).</summary>
        public bool WasLapsed;

        // ---- episode measurement (the answer the hardware round reads) -------------------------
        public bool EpisodeActive;
        public int Frames;
        public int Lapses;              // hide/show cycles ridden out inside one episode
        public float MaxStep;           // world units, largest one-frame move of the host
        public float PathSum;           // world units, total path walked while shown
        public float MaxStepHeadMove;   // world units, the HEAD's own move on that same frame
        public bool MaxStepOnResume;    // the largest step was the first frame back after a hide
        public Vector3 PrevPos;
        public Vector3 PrevHeadPos;
        public float SettleError;       // world units, |host − reading spot| on the last frame
        public float HeadDistance;      // world units, |host − head| on the last frame
        public bool ResumedThisFrame;
        public int Episodes;            // episodes begun this session — one per genuine hover
        public int ReportsLeft = EpisodeReportBudget;
        public int Reported;            // episodes reported (hard-capped, see EpisodeReportHardCap)
        public float WorstReported;      // world units, largest max-step any reported episode had
    }

    private readonly HintState _text = new() { IsTextInfo = true };
    private readonly HintState _prop = new();

    public void LateTick()
    {
        if (!WorldUIConfig.ConversionActive)
        {
            // The hosts are gone with the conversion, so there is nothing to resume onto: a hard
            // reset, not a lapse.
            Reset(_text, "TextInfoPanel", keepBinding: true);
            Reset(_prop, "PropInfoPanel", keepBinding: true);
            return;
        }

        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        FaceHint(
            Singleton<UITextInfoPanel>.IsInitialized ? Singleton<UITextInfoPanel>.Instance : null,
            _text, head, "TextInfoPanel (e.g. \"Geschlossene Tür\")");
        FaceHint(
            Singleton<UIPropInfoPanel>.IsInitialized ? Singleton<UIPropInfoPanel>.Instance : null,
            _prop, head, "PropInfoPanel (quest item)");

        // The SECOND held prop's card (a frozen copy, not a singleton window, so FaceHint never
        // sees it) gets the same late re-assert the live held card gets below: placed in Update,
        // the hands move in LateUpdate, and this step is the last in the chain — without it the
        // copy trails its hand by a frame while the live card does not.
        Surfaces.PropInfoSurface.LateTickSecondCard();
    }

    private void FaceHint(Component? panel, HintState s, Camera head, string name)
    {
        if (panel == null)
        {
            Reset(s, name);
            return;
        }

        // Cache the UIWindow across the singleton's life (both panels RequireComponent it).
        if (!ReferenceEquals(panel, s.Attached))
        {
            Reset(s, name);
            s.Attached = panel;
            s.Window = panel.GetComponent<UIWindow>();
        }

        // Only while genuinely visible — IsVisible gates on CanvasGroup alpha > 0, so the
        // hide-fade / PropInfoSurface release hysteresis is excluded. On hide: do nothing to the
        // host, but KEEP the drift for the resume grace (see FollowResumeGraceSeconds) — the
        // hover producers hide and re-show this window several times a second while the pointer
        // rests on one prop, and re-seeding on each of those is the reported twitch.
        if (s.Window == null || !s.Window.IsVisible)
        {
            Lapse(s, name);
            return;
        }

        // Resolve the world host PropInfoSurface converted this panel onto. Missing =
        // not converted (PropInfoCards off, or a frame mid-convert) → leave it be. This is a
        // LAPSE, not a hard reset: a release-and-reconvert inside the grace must resume at the
        // reading spot rather than start again from the fresh host's dock.
        Transform? host = FindHost(panel.transform);
        if (host == null)
        {
            Lapse(s, name);
            return;
        }

        // HELD-PROP CARVE-OUT (ModBuild 360) - see the class doc. Ask the surface that owns the
        // held-prop concept; do not try to reconstruct it from anything visible here. Note the
        // pose is RE-ASSERTED rather than merely skipped: PropInfoSurface placed it in Update, the
        // hands move in LateUpdate, and this step is registered last in the WorldUI chain, so
        // writing it again here is what keeps the card glued to the prop instead of trailing it by
        // a frame.
        //
        // AND THE CARVE-OUT FOLLOWS THE CARD, NOT THE WINDOW (ModBuild 366). `s.IsTextInfo` was the
        // right test only while UITextInfoPanel was the one window a held prop could raise; 364
        // gives a trap in the hand the RICH UIPropInfoPanel so it keeps its effect rows, and a
        // stand-down still keyed on the window would drag that better card straight back to the
        // centre of view. Board.FigureGrab.HeldPropCard.Owns answers "is THIS window the held
        // card?"; the other one keeps the head-follow on the very same frame.
        if (Board.FigureGrab.HeldPropCard.Owns(s.IsTextInfo)
            && Surfaces.PropInfoSurface.TryGetHeldDockPose(s.IsTextInfo,
                                                           out Vector3 heldPos, out Quaternion heldRot,
                                                           out string how, out _))
        {
            host.SetPositionAndRotation(heldPos, heldRot);
            // A hold is a different card in a different place, so it is a HARD reset, not a
            // lapse: a later hover restarts the lazy drift from the dock, not from here.
            Reset(s, name, keepBinding: true);
            if (!s.StoodDown)
            {
                s.StoodDown = true;
                // HW-VERIFY: a standing hardware question is waiting on this line - it must stay at
                // a tier the DEFAULT log level prints. scripts/check-hw-verify.py enforces it.
                VRLog.Note("WorldUI",
                    $"hex-hint head-follow STANDS DOWN for {name}: a prop is in the hand, so the card "
                    + $"rides the prop ({how}). The head-follow is unchanged for laser hover and for "
                    + "the quest-item hint. If this line is missing while a prop is held, this step "
                    + "is still dragging the card to the centre of view.");
            }
            return;
        }
        s.StoodDown = false;

        // RESUME OR RE-SEED (ModBuild 448). Re-seeding from host.position is a teleport to the
        // dock PropInfoSurface re-writes every frame; only do it when the hint has genuinely been
        // away, never for the hide/show churn the hover producers generate at frame rate.
        float now = Time.unscaledTime;
        s.ResumedThisFrame = false;
        if (!s.DriftValid || now - s.LastDriveTime > FollowResumeGraceSeconds)
        {
            FlushEpisode(s, name, "the hint was away longer than the resume grace");
            s.Pos = host.position;
            s.Rot = host.rotation;
            s.PosVel = Vector3.zero;
            s.DriftValid = true;
            BeginEpisode(s, host.position, head.transform.position);
        }
        else if (s.WasLapsed)
        {
            s.Lapses++;
            s.ResumedThisFrame = true;
        }
        s.WasLapsed = false;
        s.LastDriveTime = now;

        Vector3 readingSpot = host.position;   // the follow-off case: the dock IS the intended spot

        if (WorldUIConfig.HexHintFollowView.Value)
        {
            // Lazy follow to the center of view. Target: a comfortable reading spot in front
            // of the head, slightly below the gaze center, at a fixed distance × diorama scale.
            Transform h = head.transform;
            float worldScale = PanelLayout.WorldScale;
            // Sideways rides the head's RIGHT flattened into the horizontal plane, so a
            // lateral offset does not drift up/down when the player looks up or down.
            Vector3 flatRight = h.right;
            flatRight.y = 0f;
            flatRight = flatRight.sqrMagnitude > 1e-6f ? flatRight.normalized : Vector3.right;
            Vector3 targetPos = h.position
                                + h.forward * (FollowDistance * worldScale)
                                - Vector3.up * (FollowDrop * worldScale)
                                + flatRight * (FollowSide * worldScale);
            Quaternion targetRot = FaceHead(targetPos, h.position);
            readingSpot = targetPos;

            float dt = Time.deltaTime;
            s.Pos = Vector3.SmoothDamp(s.Pos, targetPos, ref s.PosVel, FollowSmoothTime, Mathf.Infinity, dt);
            // Frame-rate-independent exponential approach (lazy, not a hard billboard).
            s.Rot = Quaternion.Slerp(s.Rot, targetRot, 1f - Mathf.Exp(-FollowRotLambda * dt));

            host.position = s.Pos;
            host.rotation = s.Rot;
        }
        else
        {
            // Toggle off: keep PropInfoSurface's dock position, only re-face to the head.
            host.rotation = FaceHead(host.position, head.transform.position);
        }

        MeasureEpisode(s, host.position, head.transform.position, readingSpot);

        if (!s.Engaged)
        {
            s.Engaged = true;
            VRLog.Info("WorldUI", $"Hex hint follows head (lazy, center of view): {name}.");
        }
    }

    // ---- the resume grace's bookkeeping, and the episode report -------------------------------

    /// <summary>
    /// This tick did not drive the host — the window is invisible, or its converted host is gone
    /// for a frame. Keep the drift alive inside the grace; past it, the episode is over.
    /// </summary>
    private void Lapse(HintState s, string name)
    {
        s.Engaged = false;
        s.WasLapsed = true;
        if (!s.DriftValid)
            return;
        if (Time.unscaledTime - s.LastDriveTime <= FollowResumeGraceSeconds)
            return;   // still inside the grace — the hint may come straight back
        s.DriftValid = false;
        FlushEpisode(s, name, "the hint stayed away past the resume grace");
    }

    private static void BeginEpisode(HintState s, Vector3 hostPos, Vector3 headPos)
    {
        s.EpisodeActive = true;
        s.Episodes++;
        s.Frames = 0;
        s.Lapses = 0;
        s.MaxStep = 0f;
        s.PathSum = 0f;
        s.MaxStepHeadMove = 0f;
        s.MaxStepOnResume = false;
        s.PrevPos = hostPos;
        s.PrevHeadPos = headPos;
        s.SettleError = 0f;
        s.HeadDistance = 0f;
    }

    /// <summary>
    /// Per driven frame: accumulate what the RENDERED host actually did. No allocation and no
    /// log — the episode is summarised once, in <see cref="FlushEpisode"/>.
    /// </summary>
    private static void MeasureEpisode(HintState s, Vector3 hostPos, Vector3 headPos, Vector3 readingSpot)
    {
        if (!s.EpisodeActive)
            return;
        float step = Vector3.Distance(hostPos, s.PrevPos);
        float headStep = Vector3.Distance(headPos, s.PrevHeadPos);
        s.PrevPos = hostPos;
        s.PrevHeadPos = headPos;
        s.Frames++;
        if (s.Frames > 1)
        {
            s.PathSum += step;
            if (step > s.MaxStep)
            {
                s.MaxStep = step;
                s.MaxStepHeadMove = headStep;
                s.MaxStepOnResume = s.ResumedThisFrame;
            }
        }
        s.SettleError = Vector3.Distance(hostPos, readingSpot);
        s.HeadDistance = Vector3.Distance(hostPos, headPos);
    }

    /// <summary>
    /// End of a shown episode: one PLACEMENT line and one STILLNESS line, then reset. Capped at
    /// <see cref="EpisodeReportBudget"/> episodes per panel per session, so a laser sweeping the
    /// board cannot rebuild the flood ModBuild 331 removed.
    /// </summary>
    private static void FlushEpisode(HintState s, string name, string why)
    {
        if (!s.EpisodeActive)
            return;
        s.EpisodeActive = false;
        int frames = s.Frames;
        int lapses = s.Lapses;
        float scale = PanelLayout.WorldScale;
        float toMm = scale > 0f ? 1000f / scale : 0f;
        float maxMm = s.MaxStep * toMm;
        float pathMm = s.PathSum * toMm;
        float headMoveMm = s.MaxStepHeadMove * toMm;
        float settleMm = s.SettleError * toMm;
        float headCm = s.HeadDistance * toMm * 0.1f;
        bool onResume = s.MaxStepOnResume;
        if (frames < 2 || s.Reported >= EpisodeReportHardCap)
            return;
        bool newWorst = s.MaxStep > s.WorstReported;
        if (s.ReportsLeft <= 0 && !newWorst)
            return;
        if (s.ReportsLeft > 0)
            s.ReportsLeft--;
        s.Reported++;
        if (newWorst)
            s.WorstReported = s.MaxStep;

        // Which term owns the largest step. The head's own motion is subtracted first because a
        // card that holds still IN FRONT OF A MOVING PLAYER is doing its job, and a report that
        // could not tell that from drift would blame the follow for the player's neck.
        string term = onResume
            ? "the first frame back after a hide — a re-seed slipped past the resume grace"
            : headMoveMm > maxMm * 0.5f
                ? $"the player's own head, which moved {headMoveMm:0.00} mm on that same frame"
                : "the lazy SmoothDamp still converging on the reading spot";

        // HW-VERIFY: the JITTER half of the 2026-09-05 report ("Das Tooltip von einer neuen
        // gespawnten Falle zuckt"). One line per hover EPISODE, never per frame. READ IT LIKE
        // THIS: `lapse(s)` is the hide/show churn the hover producers generated while the pointer
        // rested on ONE prop — before ModBuild 448 every one of those was a teleport back to the
        // dock, so a two-digit lapse count with a small largest-step is the fix WORKING, not
        // failing. FALSIFIER: a largest one-frame move of tens of mm whose term reads "a re-seed
        // slipped past the resume grace" means the grace is too short for this producer and the
        // fix is inert; a large move with term "the lazy SmoothDamp still converging" over a long
        // episode means the card never reached the reading spot at all, which is a different
        // defect (read the PLACEMENT line's settle error). Tier enforced by check-hw-verify.py.
        VRLog.Note("WorldUI",
            $"HEX HINT STILLNESS for {name}: over {frames} shown frame(s) the card's largest "
            + $"one-frame move was {maxMm:0.00} mm ({pathMm:0.00} mm of path in total), and it "
            + $"rode out {lapses} hide/show lapse(s) without re-seeding. Largest step attributed "
            + $"to: {term}. Episode ended because {why}. This was episode {s.Episodes} for this "
            + $"panel this session and report {s.Reported} of at most {EpisodeReportHardCap}"
            + (newWorst ? " (a NEW WORST, reported whatever the routine budget says)." : ".")
            + " READ THE EPISODE COUNT FIRST: before ModBuild 448 one resting hover produced a "
            + "new episode every few frames (62 of them inside the 2026-09-05 log's single trap "
            + "hover), so a hover the player held still that reads as ONE episode with a "
            + "two-digit lapse count is the fix working.");

        // HW-VERIFY: the WRONG-PLACE half of the same report ("und ist an der falschen Stelle").
        // The settle error is the distance between where the card actually ended up and the
        // reading spot the follow was aiming at — the ONE number that says whether the card got
        // there. FALSIFIER: a settle error of tens of centimetres means the card is still being
        // caught mid-drift, i.e. it is being re-seeded from PropInfoSurface's dock and the resume
        // grace is not holding; a settle error near zero while the user still calls the position
        // wrong means the READING SPOT itself is wrong and the answer is the [WorldUI]
        // HexHintDistance/HexHintDrop/HexHintSide dials, not this code.
        VRLog.Note("WorldUI",
            $"HEX HINT PLACEMENT for {name}: the card finished {settleMm:0.0} mm from the reading "
            + $"spot it was aiming at, {headCm:0.0} cm from the eye, after {frames} shown "
            + $"frame(s). Zero is the requirement: the card must settle where every other "
            + "laser-hover hint settles, not somewhere between there and the PropInfo dock.");
    }

    /// <summary>Converted host for a panel's <see cref="ConvertedPanel.Target"/>, or null.</summary>
    private static Transform? FindHost(Transform target)
    {
        var panels = CanvasConversion.ActivePanels;
        for (int i = 0; i < panels.Count; i++)
        {
            ConvertedPanel p = panels[i];
            if (p != null && p.IsAlive && ReferenceEquals(p.Target, target))
                return p.HostTransform;
        }
        return null;
    }

    /// <summary>Upright orientation facing the head (uGUI front toward the viewer → +Z away).</summary>
    private static Quaternion FaceHead(Vector3 hostPos, Vector3 headPos)
    {
        Vector3 away = hostPos - headPos;
        away.y = 0f;
        if (away.sqrMagnitude < 1e-4f)
            away = Vector3.forward;
        else
            away.Normalize();
        return Quaternion.LookRotation(away, Vector3.up);
    }

    /// <summary>
    /// HARD reset: flush the episode and drop the drift, so the next show re-seeds from the dock.
    /// This is for the cases where resuming would be WRONG — the singleton was rebuilt, the card
    /// went into a hand, the conversion was switched off — never for the hide/show churn, which
    /// goes through <see cref="Lapse"/>.
    /// </summary>
    /// <param name="keepBinding">True to keep the cached panel/window and the stand-down edge
    /// (the held-prop branch is still bound to the same singleton and must keep its one-shot
    /// line one-shot).</param>
    private static void Reset(HintState s, string name, bool keepBinding = false)
    {
        FlushEpisode(s, name, keepBinding ? "the card went into a hand" : "the panel binding was dropped");
        s.DriftValid = false;
        s.Engaged = false;
        s.WasLapsed = false;
        if (keepBinding)
            return;
        s.Attached = null;
        s.Window = null;
        s.StoodDown = false;
    }

    public void Shutdown()
    {
        Reset(_text, "TextInfoPanel");
        Reset(_prop, "PropInfoPanel");
    }
}
