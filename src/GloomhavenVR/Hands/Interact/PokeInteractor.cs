using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Fingertip (index tip) press interaction (FROZEN Phase-2 API). Two target kinds:
///
/// 1. Registered <see cref="IPokeable"/> colliders (<see cref="VRInteractables"/>):
///    nearest collider within hover range gets OnPokeEnter/Exit; pressing into the
///    ~8 mm fingertip contact radius fires OnPoke once (re-arms after retracting).
///
/// 2. Registered uGUI canvases (<see cref="UguiPokeSurfaces"/>): when the fingertip
///    crosses a world-space canvas plane, real pointer events are synthesized via
///    ExecuteEvents (<see cref="UguiPointer"/>) — hover from the front side; plane
///    contact ARMS the press (pointerDown → the button shows its pressed visual, light
///    haptic tick) and the CLICK fires once the fingertip pushed
///    <see cref="WorldUI.WorldUIConfig.PokePressDepthMm"/> (default 12 mm) THROUGH the
///    plane — the flat-button analog of the 3D keycaps' travel-fire (user #12:
///    instant-on-contact fired on accidental brushes). Retracting before reaching the
///    depth cancels without a click; retracting past ReleaseDepth re-arms the next
///    press; depth 0 restores the old instant click on contact. Game modality is
///    respected because hits come from the canvas's own (enabled) GraphicRaycaster only.
///
///    PER-CANVAS PRESS MODE (user #13b): a canvas tagged in
///    <see cref="DeliberatePokeSurfaces"/> (the decision dock's high-stakes buttons)
///    takes the DELIBERATE v1 semantics instead — contact arms exactly the same, but
///    the click fires only on the conscious WITHDRAWAL back past ReleaseDepth in FRONT
///    of the plane; sweeping through past PressThrough or leaving sideways cancels
///    silently (no click, no cooldown charge). "You have to push in AND consciously
///    pull back" — a sweep of the hand across the dock can never trigger a decision.
///    Gated live by [WorldUI] DecisionPokeDeliberate (default true).
///
/// Plain class ticked by <see cref="VRHand"/> every frame after pose update.
/// No per-frame allocations: for-loops over registries, reused event data.
/// All distances are meters at scale 1 and multiplied by the hand's world scale.
/// </summary>
internal sealed class PokeInteractor
{
    // Distances in meters (scale 1). Canvas distances are per-canvas since P5
    // (UguiPokeSurfaces.Register(canvas, PokeSurfaceTuning) — MISSION A.10).
    //
    // MIRRORED (FingertipRadius, ReleaseRange): the near board click and three other
    // fingertip-contact sites carry their own copies of these two values so their press and
    // re-arm edges land on the SAME depths as the poke — a mismatch means the surfaces feel
    // different under one finger. Deliberately not merged into a shared constant (it would
    // either leak an interactor private onto the frozen P2 surface or hide the number in a
    // Core file nobody opens when tuning — REVIEW-Hands-Board-Core §P3). The copies are held
    // together by scripts/check-mirrors.sh, which fails the guard and NAMES every site when
    // one is tuned alone. Tune them together or document why they may now diverge.
    private const float FingertipRadius = 0.008f;
    private const float HoverRange = 0.035f;
    private const float ReleaseRange = 0.02f;

    /// <summary>
    /// Anti-double-fire (user #3/#7b): minimum time between two uGUI poke clicks of the
    /// same hand. The press is edge-triggered (armed only after the fingertip retracted
    /// past ReleaseDepth), so this only swallows re-entry jitter right at the release
    /// boundary — it is far below anything a deliberate second press can hit, so it
    /// never reads as a dwell.
    /// </summary>
    private const float ClickCooldownSeconds = 0.25f;

    /// <summary>
    /// User #12 fallback when the WorldUI config is not yet bound (module init order):
    /// the [WorldUI] PokePressDepthMm default.
    /// </summary>
    private const float DefaultPressDepthMm = 12f;

    /// <summary>
    /// The push-in fire depth must stay clearly SHORT of the per-canvas PressThrough
    /// canvas-drop guard (default 50 mm, SmallDialog 30 mm) — the click always fires
    /// before the canvas is dropped, so the guard can never cancel a press between the
    /// configured depth and PressThrough.
    /// </summary>
    private const float DepthPressThroughHeadroom = 0.8f;

    /// <summary>
    /// While a push-in press is PENDING on a canvas, its PressThrough drop guard is
    /// stretched by this factor so a fast deliberate stab that overshoots PressThrough
    /// between two frames still fires (the fire depth is evaluated on the same tick)
    /// instead of silently dropping the canvas and cancelling the press.
    /// </summary>
    private const float PendingPressThroughGrace = 1.5f;

    private readonly VRHand _hand;
    private readonly UguiPointer _pointer;

    private bool _enabled = true;
    private IPokeable? _hovered;
    private bool _armed = true;

    private Canvas? _activeCanvas;
    private bool _canvasPressed;
    private bool _pressPending;    // pointerDown sent, click awaiting fire depth / withdrawal
    private bool _pressDeliberate; // user #13b: pending press is on a deliberate (v1) canvas
    private float _lastCanvasClick = -1f;

    /// <summary>One-shot init log for the poke click mode (two hands share one line).</summary>
    private static bool s_modeLogged;

    internal PokeInteractor(VRHand hand)
    {
        _hand = hand;
        _pointer = new UguiPointer(hand.Side);
    }

    /// <summary>Configured push-in depth in meters at scale 1 (0 = instant mode). Read live.</summary>
    private static float ConfiguredPressDepthMeters()
    {
        float mm = WorldUI.WorldUIConfig.PokePressDepthMm != null
            ? WorldUI.WorldUIConfig.PokePressDepthMm.Value
            : DefaultPressDepthMm;
        return Mathf.Clamp(mm, 0f, 30f) * 0.001f;
    }

    /// <summary>
    /// One-shot mode log, emitted lazily so it reports the actually-bound config value
    /// (the Hands module may construct before the WorldUI config binds).
    /// </summary>
    private static void LogModeOnce()
    {
        if (s_modeLogged || WorldUI.WorldUIConfig.PokePressDepthMm == null)
            return;
        s_modeLogged = true;
        float mm = WorldUI.WorldUIConfig.PokePressDepthMm.Value;
        Core.VRLog.Info("Interact", mm > 0f
            ? $"PokeInteractor: PUSH-IN poke clicks — plane contact arms the press (pointerDown, pressed " +
              $"visual, light haptic tick); the CLICK fires at {mm:F0} mm push-through ([WorldUI] " +
              "PokePressDepthMm); retracting before that depth cancels without a click " +
              $"(re-arm on retract + {ClickCooldownSeconds:F2}s cooldown against double-fire)."
            : "PokeInteractor: INSTANT poke clicks ([WorldUI] PokePressDepthMm = 0) — a uGUI poke fires " +
              "the full down+up+click on plane contact " +
              $"(re-arm on retract + {ClickCooldownSeconds:F2}s cooldown against double-fire).");
    }

    /// <summary>Currently hovered pokeable, if any.</summary>
    public IPokeable? Hovered => _hovered;

    /// <summary>Currently hovered uGUI object (registered canvases only), if any.</summary>
    public GameObject? HoveredUi => _pointer.Hovered;

    /// <summary>Enable/disable (mode policy). Disabling cancels in-flight hover/press.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            if (!value)
                CancelAll();
        }
    }

    internal void Tick()
    {
        if (!_enabled || !_hand.HasPose)
        {
            return;
        }

        float scale = _hand.WorldScale;
        Vector3 tip = _hand.Rig.IndexTip.position;

        TickPokeables(tip, scale);
        TickCanvases(tip, scale);
    }

    internal void CancelAll()
    {
        if (_hovered != null)
        {
            SafeExit(_hovered);
            _hovered = null;
        }
        _armed = true;
        _pointer.Cancel();
        _activeCanvas = null;
        _canvasPressed = false;
        _pressPending = false;
        _pressDeliberate = false;
    }

    /// <summary>
    /// USER #13b: does THIS canvas take the deliberate v1 press (contact arms, click on
    /// withdrawal past ReleaseDepth, sweep-through cancels)? Registered per docked
    /// canvas by the decision dock; [WorldUI] DecisionPokeDeliberate (default true) is
    /// the live escape hatch — off restores the shared push-in behaviour for decision
    /// buttons too. Null-tolerant on config for module init order.
    /// </summary>
    private static bool IsDeliberate(Canvas canvas)
    {
        if (!DeliberatePokeSurfaces.Contains(canvas))
            return false;
        var entry = WorldUI.WorldUIConfig.DecisionPokeDeliberate;
        return entry == null || entry.Value;
    }

    // ---- collider pokeables -------------------------------------------------------------

    private void TickPokeables(Vector3 tip, float scale)
    {
        var entries = VRInteractables.Pokeables;
        IPokeable? nearest = null;
        float nearestDist = float.MaxValue;
        bool sawDeadCollider = false;

        for (int i = 0; i < entries.Count; i++)
        {
            Collider collider = entries[i].Collider;
            if (collider == null)
            {
                sawDeadCollider = true;
                continue;
            }
            if (!collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;

            float dist = Vector3.Distance(tip, collider.ClosestPoint(tip));
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = entries[i].Target;
            }
        }

        if (sawDeadCollider)
            VRInteractables.Prune();

        // Hover transition.
        IPokeable? newHover = nearest != null && nearestDist <= HoverRange * scale ? nearest : null;
        if (!ReferenceEquals(newHover, _hovered))
        {
            if (_hovered != null)
                SafeExit(_hovered);
            _hovered = newHover;
            _armed = true;
            if (_hovered != null)
            {
                _hovered.OnPokeEnter(_hand);
                _hand.SendHaptic(HapticPreset.HoverTick);
            }
        }

        // Contact (press) with re-arm hysteresis.
        if (_hovered != null)
        {
            float contact = FingertipRadius * scale;
            if (_armed && nearestDist <= contact)
            {
                _armed = false;
                _hovered.OnPoke(_hand);
                _hand.SendHaptic(HapticPreset.ClickPulse);
            }
            else if (!_armed && nearestDist > ReleaseRange * scale)
            {
                _armed = true;
            }
        }
    }

    private void SafeExit(IPokeable target)
    {
        try
        {
            target.OnPokeExit(_hand);
        }
        catch (System.Exception ex)
        {
            Core.VRLog.Error("Interact", $"OnPokeExit threw: {ex}");
        }
    }

    // ---- uGUI canvases --------------------------------------------------------------------

    private void TickCanvases(Vector3 tip, float scale)
    {
        LogModeOnce();
        var surfaces = UguiPokeSurfaces.Surfaces;

        // Find the canvas whose plane the fingertip is closest to (front side only).
        Canvas? best = null;
        float bestAbs = float.MaxValue;
        float bestSigned = 0f;
        bool sawDead = false;

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

            Transform t = canvas.transform;
            // uGUI renders facing -forward: the viewer/front side is where
            // dot(tip - canvas, forward) < 0. Fingers physically sink through the
            // plane on a press, so tolerate some press-through before dropping it.
            float signed = Vector3.Dot(tip - t.position, t.forward);
            float maxThrough = UguiPokeSurfaces.TuningFor(canvas).PressThrough * scale;
            // Depth-fire mode only: don't drop a pending press mid-stab. A DELIBERATE
            // (v1) pending press keeps the plain guard — sweeping through past
            // PressThrough must DROP the canvas and cancel silently, never click.
            if (_pressPending && !_pressDeliberate && ReferenceEquals(canvas, _activeCanvas))
                maxThrough *= PendingPressThroughGrace;
            if (signed > maxThrough)
                continue; // far behind the canvas — ignore

            // Inside the canvas rect?
            var rect = (RectTransform)t;
            Vector3 local = t.InverseTransformPoint(tip);
            if (!rect.rect.Contains(new Vector2(local.x, local.y)))
                continue;

            float abs = Mathf.Abs(signed);
            if (abs < bestAbs)
            {
                bestAbs = abs;
                bestSigned = signed;
                best = canvas;
            }
        }

        if (sawDead)
            UguiPokeSurfaces.Prune();

        // Canvas switch / loss cancels in-flight pointer state.
        if (!ReferenceEquals(best, _activeCanvas))
        {
            // Canvas lost mid-press = the v1 cancel paths for a deliberate press:
            // swept through past PressThrough or left the rect sideways → pointerUp
            // without a click, silent, no cooldown charge.
            _pointer.Cancel();
            _canvasPressed = false;
            _pressPending = false;
            _pressDeliberate = false;
            _activeCanvas = best;
        }

        if (_activeCanvas == null)
            return;

        PokeSurfaceTuning tuning = UguiPokeSurfaces.TuningFor(_activeCanvas);

        if (bestAbs > tuning.HoverRange * scale && !_canvasPressed)
        {
            _pointer.SetHovered(null);
            return;
        }

        // Project the fingertip onto the canvas plane → screen point of the canvas camera.
        Transform ct = _activeCanvas.transform;
        Vector3 onPlane = tip - ct.forward * bestSigned;
        Camera? cam = _activeCanvas.worldCamera != null ? _activeCanvas.worldCamera : Camera.main;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, onPlane);

        bool hit = _pointer.TryRaycast(_activeCanvas, screenPos, out RaycastResult top);
        GameObject? previousHover = _pointer.Hovered;
        _pointer.SetHovered(hit ? top.gameObject : null);
        if (hit && previousHover == null && _pointer.Hovered != null)
            _hand.SendHaptic(HapticPreset.HoverTick);

        // PUSH-IN press (user #12; supersedes the user #3/#7b instant click, which
        // remains as the PokePressDepthMm = 0 mode). Plane contact only ARMS the press:
        // pointerDown is sent so the button shows its native pressed visual (plus a
        // light haptic tick), and the CLICK (pointerUp over the same handler) fires
        // once the fingertip pushed PokePressDepthMm THROUGH the plane — the flat-button
        // analog of the 3D keycaps' travel-fire, so brushing a docked panel can no
        // longer trigger a decision by accident. Retracting BEFORE the fire depth
        // cancels via _pointer.Cancel() (pointerUp without click — the uGUI
        // release-outside idiom), costs nothing (no cooldown charge) and re-arms once
        // the fingertip is back out past ReleaseDepth. The fire depth is clamped safely
        // below the per-canvas PressThrough canvas-drop guard, so a press can never be
        // eaten between fire depth and PressThrough — the click always lands first.
        // Anti-double-fire stays: _canvasPressed is edge-latched until the fingertip
        // retracts past ReleaseDepth (hysteresis re-arm) and the 0.25 s cooldown
        // swallows jitter across that boundary. (Poke never drove uGUI drags — Drag()
        // is laser-only; the laser path in RayUguiDriver is unchanged.)
        float fireDepth = ConfiguredPressDepthMeters();
        if (fireDepth > 0f)
            fireDepth = Mathf.Min(fireDepth, tuning.PressThrough * DepthPressThroughHeadroom);

        if (!_canvasPressed)
        {
            if (hit && bestSigned >= -FingertipRadius * scale)
            {
                _canvasPressed = true;
                if (Time.unscaledTime - _lastCanvasClick >= ClickCooldownSeconds)
                {
                    // USER #13b: a deliberate (v1) canvas always ARMS on contact — even
                    // with PokePressDepthMm = 0 — because its click is defined by the
                    // conscious withdrawal, not by contact or push depth.
                    bool deliberate = IsDeliberate(_activeCanvas);
                    if (deliberate || fireDepth > 0f)
                    {
                        // Arm only — pressed visual now; click at depth (v3) or on
                        // withdrawal (deliberate v1).
                        _pressPending = true;
                        _pressDeliberate = deliberate;
                        _pointer.Press(screenPos);
                        _hand.SendHaptic(HapticPreset.HoverTick); // light arming tick
                    }
                    else
                    {
                        // Instant mode (PokePressDepthMm = 0): full click on contact.
                        _lastCanvasClick = Time.unscaledTime;
                        _pointer.Press(screenPos);
                        _pointer.Release(screenPos);
                        _hand.SendHaptic(HapticPreset.ClickPulse);
                    }
                }
            }
        }
        else if (_pressPending)
        {
            if (_pressDeliberate)
            {
                // DELIBERATE v1 (user #13b, decision buttons): the click fires on the
                // conscious WITHDRAWAL — the fingertip retracting back past ReleaseDepth
                // in FRONT of the plane. Push depth never fires; sweeping through past
                // PressThrough (or leaving the rect sideways) drops the canvas in the
                // scan above → Cancel, silent. Firing at the withdrawal edge means the
                // fingertip is already past the re-arm hysteresis, so the press fully
                // re-arms here (next contact presses again, cooldown still applies).
                if (bestSigned < -tuning.ReleaseDepth * scale)
                {
                    _pressPending = false;
                    _pressDeliberate = false;
                    _canvasPressed = false;
                    _lastCanvasClick = Time.unscaledTime;
                    _pointer.Release(screenPos); // up + click (released over the pressed handler)
                    _hand.SendHaptic(HapticPreset.ClickPulse); // fire pulse on the pull-out
                }
            }
            else if (bestSigned >= fireDepth * scale)
            {
                _pressPending = false;
                _lastCanvasClick = Time.unscaledTime;
                _pointer.Release(screenPos); // up + click (released over the pressed handler)
                _hand.SendHaptic(HapticPreset.ClickPulse); // stronger fire pulse
            }
            else if (bestSigned < -tuning.ReleaseDepth * scale)
            {
                // Retracted before reaching the fire depth: cancel — pointerUp without
                // a click, no cooldown charge, immediately re-armed for the next press.
                _pressPending = false;
                _canvasPressed = false;
                _pointer.Cancel();
            }
        }
        else if (bestSigned < -tuning.ReleaseDepth * scale)
        {
            _canvasPressed = false; // re-armed: the next plane contact presses again
        }
    }
}

/// <summary>
/// USER #13b: per-canvas poke PRESS-MODE registry. Canvases tagged here take the
/// DELIBERATE v1 press semantics in <see cref="PokeInteractor"/> — contact only arms
/// (pointerDown + light haptic), the CLICK fires on the conscious withdrawal back past
/// ReleaseDepth, and sweep-through/leave-sideways cancels silently — instead of the
/// shared v3 push-in depth-fire. Registered per docked canvas by
/// <c>WorldUI.Surfaces.DecisionDockSurface</c> (the take-damage burn choice, the
/// burn-confirm dialog, the short-rest Ja/Nein: accidental instant triggers on these
/// are costly and irreversible); every unregistered canvas keeps the v3 behaviour.
/// The [WorldUI] DecisionPokeDeliberate config is checked LIVE at press time by the
/// interactor, so this registry only says WHICH canvases are decision surfaces.
/// Mirrors the <see cref="UguiPokeSurfaces"/> conventions: reference-compared list,
/// allocation-free lookup, Unity-null-tolerant unregister.
/// </summary>
internal static class DeliberatePokeSurfaces
{
    private static readonly List<Canvas> Canvases = new(2);

    /// <summary>Tag a poke canvas deliberate-press (one log line per registration).</summary>
    public static void Register(Canvas canvas, string owner)
    {
        if (canvas == null)
        {
            Core.VRLog.Warn("Interact", "DeliberatePokeSurfaces.Register called with null canvas — ignored.");
            return;
        }
        if (Contains(canvas))
            return;
        Canvases.Add(canvas);
        Core.VRLog.Info("Interact", $"Poke press-mode: canvas '{canvas.name}' registered DELIBERATE for " +
                                    $"{owner} — contact arms (pressed visual + light haptic), the CLICK " +
                                    "fires on the conscious withdrawal past ReleaseDepth, sweep-through/" +
                                    "leave-sideways cancels silently ([WorldUI] DecisionPokeDeliberate).");
    }

    /// <summary>Reference-based removal (a released host canvas may already be Unity-destroyed).</summary>
    public static void Unregister(Canvas canvas)
    {
        if (canvas is not null)
            Canvases.Remove(canvas);
    }

    /// <summary>True when this canvas was tagged deliberate (reference compare, allocation-free).</summary>
    internal static bool Contains(Canvas? canvas)
    {
        if (canvas is null)
            return false;
        for (int i = 0; i < Canvases.Count; i++)
        {
            if (ReferenceEquals(Canvases[i], canvas))
                return true;
        }
        return false;
    }
}
