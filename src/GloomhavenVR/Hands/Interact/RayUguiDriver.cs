using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Far-ray uGUI pointing (P6, hardware test #8): the dominant hand's aim ray drives
/// hover/press/click on every world-space canvas registered in
/// <see cref="UguiPokeSurfaces"/> — the settings panel, converted dialogs and all
/// other world panels become laser-clickable exactly like the flat screen.
///
/// Mechanism: intersect the aim ray with each registered canvas plane (front side,
/// rect bounds); the nearest hit drives the existing <see cref="UguiPointer"/>
/// (ExecuteEvents with modality respect — a disabled GraphicRaycaster yields no
/// events) with its own pointer ID, and feeds the hit point into
/// <see cref="RayInteractor.UiHitOverride"/> so the visible beam clamps to the panel.
/// Trigger = press/release/click; the pressed canvas is latched (FlatScreen pattern)
/// so small drifts during the pull cannot cancel the click.
///
/// Priority rules:
/// - A nearer PHYSICS hit on the same ray (fan card, miniature, furniture) blocks the
///   UI hit — no clicking through objects.
/// - While this driver (or the fan laser) clamps the beam, <c>Ray.HasFreshUiHit</c> is
///   true and BoardClickDriver skips its far trigger-click — UI wins over board picks.
/// - The 2D flat screen keeps its own path (screen-space canvas, never registered in
///   UguiPokeSurfaces).
///
/// One instance per hand (created by <see cref="VRHand.Initialize"/>, ticked after the
/// ray); inert unless the hand is the dominant one and its ray interactor is enabled.
/// No per-frame allocations: for-loops over the surface registry, reused event data.
/// </summary>
internal sealed class RayUguiDriver
{
    /// <summary>Max pointing distance in meters (scale 1) — matches the ray interactor.</summary>
    private const float MaxDistanceMeters = 20f;

    /// <summary>A physics hit closer than the UI plane by more than this blocks the UI hit (meters, scale 1).</summary>
    private const float OcclusionEpsilonMeters = 0.005f;

    /// <summary>
    /// Task #8 (thumbstick scrolling): stick-Y deadzone below which no scroll fires. Deliberately
    /// generous so resting-drift on the pointing hand's stick never nudges a hovered list, and a
    /// diagonal snap-turn flick (x-axis, engage 0.7) with incidental y-tilt barely scrolls.
    /// </summary>
    private const float ScrollDeadzone = 0.3f;

    /// <summary>
    /// Task #8: scroll speed in mouse-wheel NOTCHES per second at FULL stick deflection (the
    /// PointerEventData.scrollDelta unit; each ScrollRect multiplies by its own scrollSensitivity
    /// px/notch, so the felt speed matches how that list scrolls with a real wheel). Tunable v1
    /// constant — promote to WorldUIConfig if hardware testing wants live tuning.
    /// </summary>
    private const float ScrollNotchesPerSecond = 40f;

    private readonly VRHand _hand;
    private readonly UguiPointer _pointer;

    private Canvas? _canvas;   // hovered canvas; latched while pressing
    private bool _pressing;

    internal RayUguiDriver(VRHand hand)
    {
        _hand = hand;
        _pointer = new UguiPointer(hand.Side, farRay: true);
    }

    /// <summary>True while the aim ray hits a registered UI surface this frame.</summary>
    public bool HasHit { get; private set; }

    /// <summary>Ray distance to the UI surface hit (valid while <see cref="HasHit"/>).</summary>
    public float HitDistance { get; private set; } = float.PositiveInfinity;

    /// <summary>Currently hovered uGUI object under the ray, if any.</summary>
    public GameObject? Hovered => _pointer.Hovered;

    /// <summary>
    /// The registered canvas the beam currently lands on (valid while <see cref="HasHit"/>;
    /// latched while pressing). Consumed by <c>Cards.HalfSelection</c>'s geometric half
    /// highlight: "the beam is on THIS docked card's face" must come from the SAME
    /// arbitration that delivers the uGUI events (nearest canvas, physics occlusion, fan
    /// occlusion, settings exemption) - a second ray-vs-card test could disagree with it.
    /// </summary>
    internal Canvas? HoveredCanvas => _canvas;

    internal void Tick()
    {
        // Dominant hand only (the off-hand holds the fan); dominance can switch live.
        // Ray.Active is the level-derived effective state (test #19) — while the hand
        // holds a grabbable the ray's pick is suppressed and STALE, so gate on it
        // (not just Enabled) rather than point with a frozen ray.
        if (!_hand.Ray.Active || VRHands.Primary != _hand)
        {
            Cancel();
            return;
        }

        PickPose pick = _hand.Ray.Current;
        float scale = _hand.WorldScale;

        if (_pressing && _canvas != null)
        {
            TickPressed(pick, scale);
            return;
        }
        _pressing = false;

        Canvas? best = null;
        float bestDist = MaxDistanceMeters * scale;
        Vector3 bestPoint = default;
        bool sawDead = false;

        // Settings-surface exemption (user ruling 2026-08-02, see TrySettingsFallThrough):
        // the mod-owned settings menu is tracked with its OWN distance budget — the shared
        // bestDist budget skips every canvas behind the current winner, which is exactly
        // the position the settings menu is in when a blocking modal floats in front of it.
        Canvas? settings = null;
        float settingsDist = MaxDistanceMeters * scale;
        Vector3 settingsPoint = default;

        var surfaces = UguiPokeSurfaces.Surfaces;
        for (int i = 0; i < surfaces.Count; i++)
        {
            Canvas canvas = surfaces[i];
            if (canvas == null)
            {
                sawDead = true;
                continue;
            }
            if (!canvas.isActiveAndEnabled)
                continue;
            LogCanvasOnce(canvas);
            if (TryIntersect(canvas, pick.Origin, pick.Direction, bestDist, out float dist, out Vector3 point))
            {
                best = canvas;
                bestDist = dist;
                bestPoint = point;
            }
            if (WorldUI.ModalFallback.IsSettingsSurface(canvas)
                && TryIntersect(canvas, pick.Origin, pick.Direction, settingsDist,
                    out float sDist, out Vector3 sPoint))
            {
                settings = canvas;
                settingsDist = sDist;
                settingsPoint = sPoint;
            }
        }

        if (sawDead)
            UguiPokeSurfaces.Prune();

        // Physics occlusion: something solid in front of the panel blocks the laser.
        if (best != null && pick.HasHit && pick.HitDistance < bestDist - OcclusionEpsilonMeters * scale)
            best = null;

        // Solid-occluder rule (fan cards AND the control board, user report 2026-08-04): a
        // solid mod-owned surface in front of the panel blocks the UI hit — the laser must
        // not click a menu visible THROUGH the hand of cards or select options-menu tabs
        // BEHIND the control board it visibly collides with. Mirrors the physics rule using
        // the ray's precomputed nearest solid distance (board mesh, keycaps, piles, slotted
        // cards, fan cards) — which HOLDS for a short grace after the beam leaves the
        // occluder (the trigger-pull jerk, see RayInteractor.SolidOccluderDistance), so a
        // press born mid-pull can never sneak a hover + pointer-down onto the panel for its
        // one off-occluder frame. The shared epsilon keeps the board's own docked/converted
        // surfaces (initiative track, control dock, slot-card faces — coplanar with or proud
        // of the board colliders) out of their own occluder's shadow.
        Canvas? solidOccluded = null;
        if (best != null && _hand.Ray.SolidOccluderDistance < bestDist - OcclusionEpsilonMeters * scale)
        {
            // [Optimize] LeanLogStrings: skip the per-frame string build when the note is throttled.
            if (RayInteractor.WantFanOcclusionNote)
                _hand.Ray.NoteFanOcclusion($"uGUI panel '{best.name}'", bestDist);
            solidOccluded = best;
            best = null;
        }

        // SETTINGS-SURFACE EXEMPTION (user ruling 2026-08-02: the mod's settings menu "soll
        // nie geblockt werden von irgendwas"): when the nearest-canvas winner is NOT the
        // settings surface but the settings surface lies farther along the SAME ray, and the
        // winner has no interactive widget under the beam point (transparent host apron,
        // inert backing/blocker graphics — the exact regions a gaze-parked BLOCKING level
        // message put between the hand and the open settings menu), hover/press fall THROUGH
        // to the settings surface instead of being silently eaten. Real widgets on the nearer
        // window (a dismiss button, the story skip area, its scroll/drag areas) still win —
        // blocking semantics for game surfaces stay byte-identical; only presses that today
        // die on nothing are redirected, and only INTO the mod-owned settings UI.
        if (best != null && settings != null && !ReferenceEquals(best, settings)
            && settingsDist >= bestDist
            && TrySettingsFallThrough(best, bestPoint, settingsDist, in pick, scale))
        {
            LogSettingsExemption(best, bestDist, settings, settingsDist);
            best = settings;
            bestDist = settingsDist;
            bestPoint = settingsPoint;
        }

        if (!ReferenceEquals(best, _canvas))
        {
            _pointer.Cancel();
            _canvas = best;
        }

        if (_canvas == null)
        {
            HasHit = false;
            HitDistance = float.PositiveInfinity;
            // Press-ownership evidence (user round 2, item fan vs initiative portraits; board
            // 2026-08-04): a TRIGGER press this frame that would have gone to a panel BEHIND
            // the fan/board is fully consumed here — no hover was raised above, and without a
            // press no pointer-up/click can follow on release. Info (not Debug) on purpose:
            // BepInEx's default disk config drops Debug, and this line is the attribution the
            // next hardware log needs. Unthrottled but edge-only (one line per suppressed
            // press, not per frame).
            if (solidOccluded != null && _hand.TriggerDown)
                Core.VRLog.Info("Interact", $"{_hand.Side} trigger PRESS on uGUI panel '{solidOccluded.name}' " +
                                            $"SUPPRESSED — {(_hand.Ray.SolidOccluderIsBoard ? "the control board" : "the raised card fan")} " +
                                            $"owns this press (solid surface at {_hand.Ray.SolidOccluderDistance:F2} m" +
                                            $"{(!_hand.Ray.SolidOccluderIsBoard && _hand.Ray.FanOccluderHeld ? ", pull-jerk hold" : "")}); " +
                                            "no pointer-down/click reaches the panel behind it.");
            return;
        }

        HasHit = true;
        HitDistance = bestDist;
        _hand.Ray.UiHitOverride = bestPoint; // beam clamps to the panel (one-frame latch)

        Vector2 screenPos = ToScreen(_canvas, bestPoint);
        bool hit = _pointer.TryRaycast(_canvas, screenPos, out RaycastResult top);
        GameObject? previous = _pointer.Hovered;
        _pointer.SetHovered(hit ? top.gameObject : null);
        if (_pointer.Hovered != null && !ReferenceEquals(_pointer.Hovered, previous))
            _hand.SendHaptic(HapticPreset.HoverTick); // debounced: only on hover change

        TickStickScroll(); // task #8: pointing hand's thumbstick scrolls a hovered ScrollRect

        // SETTINGS CLICK TRACE (user ruling 2026-08-02, round 3): every trigger press landing on
        // the mod settings surface logs the full delivery state — after two blind hardware
        // rounds this line makes the next log decisive about WHERE a dead click died, whatever
        // the cause turns out to be (see LogSettingsClickTrace).
        if (_hand.TriggerDown && WorldUI.ModalFallback.IsSettingsSurface(_canvas))
            LogSettingsClickTrace(hit, top);

        if (_hand.TriggerDown && hit && _hand.Grabber.Held == null)
        {
            _pressing = true;
            _pointer.Press(screenPos);
            _hand.SendHaptic(HapticPreset.ClickPulse);
        }
    }

    /// <summary>
    /// Task #8 (thumbstick scrolling): while the beam hovers uGUI that sits inside a
    /// <see cref="ScrollRect"/>, the POINTING hand's thumbstick Y scrolls it — a
    /// synthesized mouse-wheel notch stream via the established ExecuteEvents pattern
    /// (<see cref="UguiPointer.Scroll"/>), so ScrollRects (settings lists, the
    /// compendium, an open dropdown's item list) respond exactly as to a wheel.
    ///
    /// Stick contention: this reads ONLY the Y axis and ONLY while actually hovering a
    /// scrollable; SnapTurn/AoE read the X axis (SnapTurn engages at |x| ≥ 0.7), so
    /// neither consumes the other's input — a deliberate straight-up push scrolls, a
    /// deliberate sideways flick turns. Unscaled time: menus pause the game clock.
    ///
    /// <para>STICK FLIGHT reads the SAME y axis, and it is the one control that genuinely
    /// collides here (user 2026-08-03: scrolling a menu also flew the player forward). The
    /// scrollable resolution below is therefore hoisted ABOVE the deadzone test and published
    /// through <see cref="UiScrollFocus"/>: this driver already has to decide "is the thing
    /// under this hand's beam scrollable" in order to deliver the wheel, so that decision is
    /// the authoritative answer to the same question flight needs, and taking it from anywhere
    /// else would be a second opinion that could disagree. Hoisting it costs one hierarchy walk
    /// per hovering frame on the dominant hand and buys the correct behaviour in the band
    /// between the flight deadzone (0.2) and the scroll deadzone (0.3), where a naive
    /// "suppress only while scrolling" rule would still fly.</para>
    /// </summary>
    private void TickStickScroll()
    {
        GameObject? hovered = _pointer.Hovered;
        if (hovered == null)
            return;
        ScrollRect? scrollable = hovered.GetComponentInParent<ScrollRect>();
        if (scrollable == null || !scrollable.isActiveAndEnabled)
            return;
        // The beam is on a list that WOULD move — this hand's stick belongs to scrolling now,
        // whether or not the player has already pushed it (see UiScrollFocus). Note that
        // CanScroll gates only the ARBITRATION, never the delivery below: the wheel keeps being
        // sent exactly as before, so a ScrollRect this predicate misjudges loses nothing.
        bool live = UiScrollFocus.CanScroll(scrollable);
        if (live)
            UiScrollFocus.NoteScrollHover(_hand, scrollable, nameof(RayUguiDriver));

        float y = _hand.Thumbstick.y;
        if (Mathf.Abs(y) < ScrollDeadzone)
            return;
        // Deadzone-normalized response, so speed ramps smoothly from 0 at the deadzone
        // edge to ScrollNotchesPerSecond at full deflection.
        float response = (Mathf.Abs(y) - ScrollDeadzone) / (1f - ScrollDeadzone);
        float notches = Mathf.Sign(y) * response * ScrollNotchesPerSecond * Time.unscaledDeltaTime;
        _pointer.Scroll(new Vector2(0f, notches));
        if (live)
            UiScrollFocus.NoteScrollDelivered(_hand, scrollable, nameof(RayUguiDriver));
    }

    /// <summary>
    /// Latched press (FlatScreen pattern): keep driving the pressed canvas until the
    /// trigger releases, clamping the ray's plane hit into the canvas rect so drift
    /// off the edge cannot orphan the press.
    /// </summary>
    private void TickPressed(in PickPose pick, float scale)
    {
        Canvas canvas = _canvas!;
        if (canvas == null || !canvas.isActiveAndEnabled)
        {
            Cancel();
            return;
        }

        var rect = (RectTransform)canvas.transform;
        Transform t = rect;
        float denom = Vector3.Dot(pick.Direction, t.forward);
        Vector3 point;
        if (Mathf.Abs(denom) > 1e-5f)
        {
            float dist = Vector3.Dot(t.position - pick.Origin, t.forward) / denom;
            point = pick.Origin + pick.Direction * Mathf.Max(0f, dist);
            HitDistance = Mathf.Max(0f, dist);
        }
        else
        {
            point = t.position;
            HitDistance = Vector3.Distance(pick.Origin, point);
        }

        // Clamp into the rect (local space) so events stay on the panel.
        Vector3 local = t.InverseTransformPoint(point);
        Rect r = rect.rect;
        local.x = Mathf.Clamp(local.x, r.xMin, r.xMax);
        local.y = Mathf.Clamp(local.y, r.yMin, r.yMax);
        local.z = 0f;
        point = t.TransformPoint(local);

        HasHit = true;
        _hand.Ray.UiHitOverride = point;

        Vector2 screenPos = ToScreen(canvas, point);
        bool hit = _pointer.TryRaycast(canvas, screenPos, out RaycastResult top);
        _pointer.SetHovered(hit ? top.gameObject : null);

        TickStickScroll(); // task #8: stick-scroll stays live during a held trigger too

        // Drive drag with the SAME clamped-into-rect screen point used for the raycast,
        // so a Slider/scrollbar/scroll-rect handle moves under the sweeping laser (it
        // only responds to OnDrag). Before the release check so a held-and-moving ray
        // forwards a drag on every frame it stays held.
        _pointer.Drag(screenPos);

        // Trigger STATE, not the up edge — a mode/hands hiccup must still release.
        if (!_hand.TriggerPressed)
        {
            _pressing = false;
            _pointer.Release(screenPos);
        }
    }

    // ---- settings-surface exemption (user ruling 2026-08-02) ---------------------------

    /// <summary>Next unscaled time the settings-exemption Info line may log (shared across hands
    /// — the redirect is a steady state while the beam crosses the blocking float, so the proof
    /// line is throttled the same way the fan-occlusion note is).</summary>
    private static float s_nextSettingsExemptLogAt;

    /// <summary>
    /// Should this frame's hover/press fall through the nearest-canvas winner to the mod-owned
    /// settings surface behind it? True only when ALL of:
    /// <list type="bullet">
    /// <item>no solid physics hit, no raised card fan and no control board sits in front of the
    /// SETTINGS surface (honest physical occlusion is not a modal lock — pointing through a
    /// miniature, the hand of cards or the board stays impossible, exactly as for every other
    /// canvas);</item>
    /// <item>the winner has NO interactive widget under the beam point — the probe raycast
    /// either misses entirely (transparent host apron around a floated modal's visible
    /// content) or lands on a graphic with no click/drag/scroll handler anywhere above it
    /// (an inert backing image or a raycast-target blocker without behavior). A real widget
    /// (dismiss button, story skip area, sliders, scroll viewports) keeps the winner —
    /// blocking windows keep their own surface byte-identical.</item>
    /// </list>
    /// WHY: without this, such a press is consumed by the nearer canvas and simply DIES — the
    /// documented hardware failure ("Debug"/settings tabs unclickable while a scripted
    /// instruction floated in front of the open settings menu). The redirect is scoped to the
    /// settings surface as the TARGET only (see <c>ModalFallback.IsSettingsSurface</c>);
    /// game-canvas-vs-game-canvas arbitration is untouched.
    /// </summary>
    private bool TrySettingsFallThrough(Canvas winner, Vector3 winnerPoint, float settingsDist,
        in PickPose pick, float scale)
    {
        // Physical occlusion in front of the SETTINGS plane (same epsilon rules as the
        // winner checks above): something solid — a physics hit, the raised card fan or the
        // control board (SolidOccluderDistance) — honestly blocks it.
        if (pick.HasHit && pick.HitDistance < settingsDist - OcclusionEpsilonMeters * scale)
            return false;
        if (_hand.Ray.SolidOccluderDistance < settingsDist - OcclusionEpsilonMeters * scale)
            return false;

        // Probe the winner at the beam point: does anything INTERACTIVE actually sit there?
        if (!_pointer.TryRaycast(winner, ToScreen(winner, winnerPoint), out RaycastResult top)
            || top.gameObject == null)
            return true; // nothing at all under the beam — the press would die on the apron
        GameObject hit = top.gameObject;
        return ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit) == null
               && ExecuteEvents.GetEventHandler<IDragHandler>(hit) == null
               && ExecuteEvents.GetEventHandler<IScrollHandler>(hit) == null;
    }

    /// <summary>
    /// Hardware-log proof line for the settings exemption (user ruling 2026-08-02): names the
    /// bypassed canvas, whether a BLOCKING modal lock is active right now (the incident
    /// condition), and where the hover/press landed instead. Info on purpose — BepInEx's
    /// default disk config drops Debug, and this line is what the next hardware log needs to
    /// show the exemption engaging. Throttled (steady state while the beam crosses the float).
    /// </summary>
    private void LogSettingsExemption(Canvas bypassed, float bypassedDist, Canvas settings,
        float settingsDist)
    {
        if (Time.unscaledTime < s_nextSettingsExemptLogAt)
            return;
        s_nextSettingsExemptLogAt = Time.unscaledTime + 1f;
        bool blocking = WorldUI.ModalFallback.BlockingWindowModalActive;
        Core.VRLog.Info("Interact",
            $"SETTINGS EXEMPT: {_hand.Side} laser fell through '{bypassed.name}' " +
            $"({bypassedDist:F2} m, no interactive widget under the beam" +
            $"{(blocking ? "; BLOCKING modal lock active" : "")}) to the mod settings surface " +
            $"'{settings.name}' ({settingsDist:F2} m) — the settings menu is never input-blocked " +
            "(user ruling 2026-08-02).");
    }

    /// <summary>Next unscaled time a settings click trace may log (shared across hands): the
    /// trace fires on the press EDGE only, but a double-fire (both hands, jittered re-press)
    /// must not double the multi-line payload.</summary>
    private static float s_nextClickTraceAt;

    /// <summary>
    /// SETTINGS CLICK TRACE (user ruling 2026-08-02, round 3 diagnostics): one Info line per
    /// trigger press that lands on the mod settings surface, carrying everything needed to
    /// attribute a dead click from the log alone:
    /// <list type="bullet">
    /// <item>the FULL path of the top raycast hit (or the explicit no-hit case, where a press
    /// dies on the canvas plane with no widget under the beam);</item>
    /// <item>the resolved click handler (<c>ExecuteEvents.GetEventHandler</c> — the exact object
    /// UguiPointer will deliver pointerClick to on release) — "none" means the press cannot
    /// become a click at all;</item>
    /// <item>the nearest Selectable's <c>interactable</c> flag AND its effective
    /// <c>IsInteractable()</c> (false here with interactable=true = a CanvasGroup gate);</item>
    /// <item>the ancestor CanvasGroup chain with each group's interactable/blocksRaycasts —
    /// the candidate the game-side window stack would use to soft-disable the window;</item>
    /// <item>the game's InteractabilityManager state: whether the global veto is armed
    /// (<c>ShouldTryPreventControl</c>) and the per-widget gate verdict for the handler's widget
    /// type — AFTER the mod's SettingsClickExemption postfix, so "gate ARMED, verdict allowed"
    /// is the exemption proving itself, while "verdict VETOED" pinpoints a still-active game
    /// gate.</item>
    /// </list>
    /// WHY so heavy: two hardware rounds died blind because the game's own veto only logs to the
    /// Unity log, which BepInEx does not capture here — this line is the mod-side replacement.
    /// Edge-only (trigger press) + throttled; allocations are fine at that rate.
    /// </summary>
    private void LogSettingsClickTrace(bool hit, in RaycastResult top)
    {
        if (Time.unscaledTime < s_nextClickTraceAt)
            return;
        s_nextClickTraceAt = Time.unscaledTime + 0.25f;

        if (!hit || top.gameObject == null)
        {
            Core.VRLog.Info("Interact",
                $"SETTINGS CLICK TRACE: {_hand.Side} press on settings surface — NO uGUI hit " +
                "under the beam (raycast miss: the press dies on the canvas plane, no widget, " +
                "no handler).");
            return;
        }

        GameObject go = top.gameObject;

        // A held grabbable consumes the trigger — the press below is skipped entirely, and the
        // trace must say so or a "dead click" would be mis-attributed to the widget chain.
        string delivery = _hand.Grabber.Held == null
            ? "press delivered"
            : "press NOT delivered (hand holds a grabbable)";

        // Full path, leaf-last, so the log names the widget inside its window unambiguously.
        var path = new StringBuilder(128);
        BuildTransformPath(go.transform, path);

        GameObject? clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);

        Selectable? selectable = go.GetComponentInParent<Selectable>();
        string selectableInfo = selectable == null
            ? "none"
            : $"'{selectable.name}' interactable={selectable.interactable} " +
              $"effective={selectable.IsInteractable()}";

        // Ancestor CanvasGroup chain (leaf → root): the mechanism a game-side window stack
        // would use to soft-disable a background window without touching raycasters.
        var groups = new StringBuilder(64);
        for (Transform? t = go.transform; t != null; t = t.parent)
        {
            CanvasGroup? cg = t.GetComponent<CanvasGroup>();
            if (cg == null)
                continue;
            if (groups.Length > 0)
                groups.Append(", ");
            groups.Append('\'').Append(t.name).Append("'(i=").Append(cg.interactable ? 1 : 0)
                  .Append(",b=").Append(cg.blocksRaycasts ? 1 : 0)
                  .Append(cg.ignoreParentGroups ? ",ignoreParent" : string.Empty).Append(')');
        }

        Core.VRLog.Info("Interact",
            $"SETTINGS CLICK TRACE: {_hand.Side} press → '{path}' ({delivery}) | " +
            $"clickHandler={(clickHandler != null ? $"'{clickHandler.name}'" : "NONE")} | " +
            $"selectable={selectableInfo} | canvasGroups=[{groups}] | {DescribeGameGate(clickHandler)}.");
    }

    /// <summary>Leaf-last '/'-joined transform path (root/…/leaf) for the trace line.</summary>
    private static void BuildTransformPath(Transform leaf, StringBuilder into)
    {
        if (leaf.parent != null)
        {
            BuildTransformPath(leaf.parent, into);
            into.Append('/');
        }
        into.Append(leaf.name);
    }

    /// <summary>
    /// The game-side InteractabilityManager verdict for the widget that will receive the click:
    /// global veto armed? and, for the five gated widget families, the exact ShouldAllowClickFor*
    /// result (evaluated through the mod's SettingsClickExemption postfix — see the trace doc).
    /// Reflection-free direct calls (repo-normal for game types); exception-guarded so a game
    /// update can never turn the diagnostic into a crash.
    /// </summary>
    private static string DescribeGameGate(GameObject? clickHandler)
    {
        try
        {
            bool armed = InteractabilityManager.ShouldTryPreventControl();
            string verdict;
            if (clickHandler == null)
            {
                verdict = "no handler to gate";
            }
            else if (clickHandler.GetComponent<ExtendedToggle>() is { } et)
            {
                verdict = $"ExtendedToggle allow={InteractabilityManager.ShouldAllowClickForExtendedToggle(et)}";
            }
            else if (clickHandler.GetComponent<ExtendedButton>() is { } eb)
            {
                verdict = $"ExtendedButton allow={InteractabilityManager.ShouldAllowClickForExtendedButton(eb)}";
            }
            else if (clickHandler.GetComponent<UITab>() is { } tab)
            {
                verdict = $"UITab allow={InteractabilityManager.ShouldAllowClickForTab(tab)}";
            }
            else if (clickHandler.GetComponent<TrackedToggle>() is { } tt)
            {
                verdict = $"TrackedToggle allow={InteractabilityManager.ShouldAllowClickForTrackedToggle(tt)}";
            }
            else if (clickHandler.GetComponent<TrackedButton>() is { } tb)
            {
                verdict = $"TrackedButton allow={InteractabilityManager.ShouldAllowClickForTrackedButton(tb)}";
            }
            else
            {
                verdict = "ungated widget type (plain uGUI)";
            }
            return $"gameGate={(armed ? "ARMED" : "off")}, {verdict}";
        }
        catch (System.Exception e)
        {
            return $"gameGate=probe threw {e.GetType().Name}";
        }
    }

    // Reused corner buffer (GetWorldCorners fills in place — no per-frame allocations).
    private static readonly Vector3[] Corners = new Vector3[4];

    // World-rect verification log: once per registered canvas AND once more per
    // material size change — converted hosts can be re-fit to their visible
    // content ~0.4 s after conversion (CanvasConversion.FitHostToContent), so the
    // first-seen rect may not be the final one. Keyed by instance ID.
    private static readonly Dictionary<int, Vector2> LoggedCanvasSizes = new();

    /// <summary>
    /// Ray ∩ canvas via the RectTransform's actual WORLD-SPACE corners (test #13):
    /// pivot/sizeDelta assumptions do not enter — the plane is spanned by the real
    /// corners (GetWorldCorners: 0=bottom-left, 1=top-left, 2=top-right,
    /// 3=bottom-right), so converted windows (host rect + re-anchored child, e.g.
    /// the story window at 1920x1080 with host scale 0.7) intersect over their
    /// ENTIRE surface, including under parent shear / negative scale.
    /// </summary>
    private static bool TryIntersect(Canvas canvas, Vector3 origin, Vector3 direction,
        float maxDist, out float dist, out Vector3 point)
    {
        dist = 0f;
        point = default;

        var rect = (RectTransform)canvas.transform;
        rect.GetWorldCorners(Corners);
        Vector3 right = Corners[3] - Corners[0]; // world-space +X edge
        Vector3 up = Corners[1] - Corners[0];    // world-space +Y edge
        float rightLen2 = right.sqrMagnitude;
        float upLen2 = up.sqrMagnitude;
        if (rightLen2 < 1e-12f || upLen2 < 1e-12f)
            return false; // degenerate rect (zero size / not laid out yet)

        // uGUI faces -normal (viewer side); a ray coming FROM the viewer side travels
        // along +normal: require denom > 0 (back-side pointing never hits).
        Vector3 normal = Vector3.Cross(right, up).normalized; // == canvas forward
        float denom = Vector3.Dot(direction, normal);
        if (denom < 1e-5f)
            return false;

        dist = Vector3.Dot(Corners[0] - origin, normal) / denom;
        if (dist <= 0f || dist >= maxDist)
            return false;

        point = origin + direction * dist;
        Vector3 d = point - Corners[0];
        float u = Vector3.Dot(d, right) / rightLen2;
        float v = Vector3.Dot(d, up) / upLen2;
        return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
    }

    /// <summary>
    /// Verification log per canvas (test #13): its actual world rect — once on first
    /// sight and again whenever the world SIZE changes by more than ~1 cm (converted
    /// hosts get re-fit to their visible content shortly after conversion).
    /// </summary>
    private static void LogCanvasOnce(Canvas canvas)
    {
        var rect = (RectTransform)canvas.transform;
        rect.GetWorldCorners(Corners);
        float w = (Corners[3] - Corners[0]).magnitude;
        float h = (Corners[1] - Corners[0]).magnitude;
        int id = canvas.GetInstanceID();
        // Relative threshold: world-grab rescales every panel with the diorama, so an
        // absolute 1 cm gate re-logged hundreds of lines per session (test #14).
        if (LoggedCanvasSizes.TryGetValue(id, out Vector2 last)
            && Mathf.Abs(last.x - w) < last.x * 0.02f + 0.001f
            && Mathf.Abs(last.y - h) < last.y * 0.02f + 0.001f)
            return;
        LoggedCanvasSizes[id] = new Vector2(w, h);
        Core.VRLog.Debug("Interact",
            $"Ray-uGUI canvas '{canvas.name}': world rect {w:F3}x{h:F3} m, " +
            $"BL={Corners[0]:F3} TL={Corners[1]:F3} TR={Corners[2]:F3} BR={Corners[3]:F3}, " +
            $"pivot={rect.pivot}, sizeDelta={rect.sizeDelta}, lossyScale={rect.lossyScale:F4}.");
    }

    private static Vector2 ToScreen(Canvas canvas, Vector3 worldPoint)
    {
        Camera? cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        return RectTransformUtility.WorldToScreenPoint(cam, worldPoint);
    }

    /// <summary>Abort in-flight hover/press (hand lost, ray disabled, dominance switch).</summary>
    internal void Cancel()
    {
        if (_canvas != null || _pressing)
        {
            _pointer.Cancel();
            _canvas = null;
            _pressing = false;
        }
        HasHit = false;
        HitDistance = float.PositiveInfinity;
    }
}
