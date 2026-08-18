using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    // ---- pointer ---------------------------------------------------------------------------

    private void TickPointer()
    {
        if (_quad == null || _rt == null)
            return;

        // Handedness switch first: it may change which hand is "primary" below.
        if (TickHandednessSwitch())
        {
            HideReticle();
            return; // masks re-apply this frame; pointer resumes next frame
        }

        TickPoke();

        VRHand? hand = VRHands.Primary;
        IPickProvider? pick = VRHands.PrimaryPick;
        if (pick == null || hand == null || !pick.TryGetPick(out PickPose pose))
        {
            HideReticle();
            return;
        }

        if (_pokePressing)
        {
            // The fingertip owns the virtual mouse; the ray resumes after withdraw.
            HideReticle();
            return;
        }

        // Ray ∩ screen plane (quad faces -Z; plane normal = -forward toward viewer).
        Transform t = _quad.transform;
        Vector3 normal = -t.forward;
        float denom = Vector3.Dot(pose.Direction, normal);
        if (Mathf.Abs(denom) < 1e-4f)
        {
            HideReticle();
            return;
        }
        float dist = Vector3.Dot(t.position - pose.Origin, normal) / denom;
        if (dist < 0f)
        {
            HideReticle();
            return;
        }

        // MENU-POPUP FLOAT arbitration (unconditional — the [WorldUI] MenuPopupFloat kill switch
        // was removed by user ruling 2026-08-11): while the dominant laser is on a WORLD-SPACE
        // uGUI panel NEARER than the screen plane (the floated GlobalErrorMessage box spawns
        // 1.2 m ahead, straight in front of the ~1.6 m screen), that panel owns hover AND trigger via
        // RayUguiDriver. Without this yield the SAME trigger also warped+pressed the virtual
        // mouse at the screen pixel BEHIND the panel — answering the floated box would blindly
        // click whatever the menu shows behind it. HasHit may be one frame stale (driver tick
        // order), which only flickers the reticle at the panel edge; an already-latched screen
        // press is never abandoned mid-press (its release must still reach uGUI).
        if (!_pressing && hand.RayUgui.HasHit && hand.RayUgui.HitDistance < dist - 0.005f)
        {
            HideReticle();
            return;
        }

        Vector3 hit = pose.Origin + pose.Direction * dist;
        Vector3 local = t.InverseTransformPoint(hit); // quad local: x/y in [-0.5, 0.5]
        bool onQuad = Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f;
        if (!onQuad && !_pressing)
        {
            HideReticle();
            return;
        }
        // While pressed, edge tremor must not cancel the press — clamp instead of drop.
        local.x = Mathf.Clamp(local.x, -0.5f, 0.5f);
        local.y = Mathf.Clamp(local.y, -0.5f, 0.5f);

        // UV → virtual mouse pixels.
        var pixel = new Vector2((local.x + 0.5f) * _rt.width, (local.y + 0.5f) * _rt.height);

        // On the campaign map, a moving held trigger PANS the map (grab-drag) instead of
        // dragging a uGUI widget — see the map-pan block below. Suppress the generic
        // latch→uGUI-drag here so the two gestures never fight.
        bool mapActive = _stereo.MapActive;

        // Thumbstick scroll: the pointing hand's stick-Y scrolls the list under the laser,
        // exactly like it does on the world-space panels during a scenario. Placed before
        // the press/drag blocks so it works whether or not the trigger is held.
        TickStickScroll(pixel, hand, mapActive);

        // Click latch (requirement 1, class doc): while pressed and latched the warp
        // position stays frozen at the press pixel; deliberate sustained ray movement
        // opens the latch into a real drag.
        if (_pressing && _latched && !mapActive)
        {
            float angle = Vector3.Angle(_pressDirection, pose.Direction);
            if (angle > WorldUIConfig.DragUnlockDegrees.Value)
            {
                if (_dragOverSince < 0f)
                {
                    _dragOverSince = Time.unscaledTime;
                }
                else if (Time.unscaledTime - _dragOverSince >= WorldUIConfig.DragUnlockSeconds.Value)
                {
                    _latched = false;
                    // Deliberate drag, through uGUI's own drag events — UNCONDITIONALLY since
                    // [WorldUI] ClickMode was removed (user ruling 2026-08-13). The alternative
                    // it used to offer (press-and-follow the virtual-mouse device) never moved a
                    // menu slider on hardware: the virtual-mouse BUTTON edges do not survive the
                    // input module (test #7 — the very reason clicks went to ExecuteEvents), so a
                    // held VM "drag" carried no pressed state, uGUI started no drag, and the
                    // slider handle never followed — only the DirectClick on release ever set a
                    // value (user report: sliders in the MAIN MENU can only be clicked, never
                    // dragged, so 0 is unreachable). This path moves Sliders/Scrollbars/
                    // ScrollRects the same way the in-game world-space menus do
                    // (Hands.Interact.UguiPointer).
                    BeginScreenDrag(_latchedPixel);
                    UpdateScreenDrag(pixel); // catch the drag up to the current ray at once
                    VRLog.Info("WorldUI", $"FlatScreen pointer: click latch OPENED → drag " +
                                          $"(ray {angle:F1}° off the press direction for " +
                                          $">{WorldUIConfig.DragUnlockSeconds.Value:F2}s).");
                }
            }
            else
            {
                _dragOverSince = -1f;
            }
        }

        // MAP PAN (grab-drag): on the campaign map a held trigger that travels past a small
        // pixel threshold grabs the parchment and drags it — the map follows the laser. This
        // consumes the press latch so release does NOT select a location; a still trigger keeps
        // the latch closed and clicks through to the location, exactly as before.
        if (mapActive && _pressing)
        {
            if (!_mapPanGesture &&
                (pixel - _latchedPixel).sqrMagnitude > MapPanStartPixels * MapPanStartPixels)
            {
                _mapPanGesture = true;
                _latched = false;             // consume the click — release must not DirectClick
                EndScreenDrag();
                _stereo.BeginMapPan(_latchedPixel);
                VRLog.Info("WorldUI", "FlatScreen pointer: map PAN started (trigger-drag grabbed the map).");
            }
            if (_mapPanGesture)
                _stereo.UpdateMapPan(pixel);
        }

        // While frozen, keep re-warping to the SAME latched pixel: identical uGUI
        // position (no drag delta), but the per-tick write keeps pointer currency
        // reclaimed and the queued-event stream alive during a held press. During a map
        // pan the virtual mouse is FROZEN at the press pixel so panning never drags the
        // cursor across (and accidentally hovers) location markers.
        bool frozen = _pressing && _latched;
        if (!_mapPanGesture)
            VirtualMouse.WarpTo(frozen ? _latchedPixel : pixel);

        // uGUI drag (the only delivery path since [WorldUI] ClickMode was removed): once the
        // latch has opened, drive the uGUI IDragHandler under the press so sliders/scrollbars/
        // scroll-rects follow the ray continuously — down/up/click alone never move a Slider.
        if (_screenDragActive)
            UpdateScreenDrag(pixel);

        // Single convergent visual (test #7): the beam is CLAMPED to this exact world
        // point and the RayInteractor's reticle shows there — no separate FlatScreen
        // dot, no beam passing through the screen, no beam/dot parallax. While
        // latched, the point is the frozen click position, so the beam visibly
        // sticks to where the click will land.
        Vector3 uiWorldPoint = frozen
            ? t.TransformPoint(new Vector3(_latchedLocal.x, _latchedLocal.y, 0f))
            : hit;
        hand.Ray.UiHitOverride = uiWorldPoint;

        // Trigger = left mouse button (press/release so drags work). The release
        // condition is the trigger STATE, not the TriggerUp edge: a hands rebuild
        // mid-press (HandsDriver re-creates VRHand instances on rig changes) would
        // swallow the edge forever and leave uGUI in drag state — hover would die
        // globally (I3 hardening; VirtualMouse has a second, time-based watchdog).
        if (hand.TriggerDown && !_pressing)
        {
            _pressing = true;
            // ALWAYS LATCHED ([WorldUI] ClickLatch removed, user ruling 2026-08-13): press and
            // release must land on the same pixel or uGUI sees a tremor drag and no click at all.
            _latched = true;
            _latchedLocal = new Vector2(local.x, local.y);
            _latchedPixel = pixel;
            _pressDirection = pose.Direction;
            _dragOverSince = -1f;
            VirtualMouse.WarpTo(pixel); // press lands exactly on the frozen pixel
            LogUnderPointer(pixel); // diagnostic: what the click will actually hit
            VRLog.Info("WorldUI", $"FlatScreen pointer: trigger PRESS at RT pixel " +
                                  $"({pixel.x:F0},{pixel.y:F0}).");
        }
        else if (_pressing && !hand.TriggerPressed)
        {
            _pressing = false;
            if (!hand.TriggerUp)
                VRLog.Warn("WorldUI", "FlatScreen pointer: trigger release edge was missed " +
                                      "(hands rebuilt mid-press?) — forced release.");
            // A map pan consumed the press (latch already false) — end it, no click fires.
            if (_mapPanGesture)
            {
                _stereo.EndMapPan();
                _mapPanGesture = false;
            }
            // End any execute-mode drag first (endDrag + pointerUp). A drag opened the
            // latch, so _latched is false here and DirectClick does not double-fire —
            // the two paths are mutually exclusive (tap → DirectClick; drag → EndScreenDrag).
            EndScreenDrag();
            if (_latched)
                DirectClick(_latchedPixel);
            VRLog.Info("WorldUI", $"FlatScreen pointer: trigger RELEASE at RT pixel " +
                                  $"({pixel.x:F0},{pixel.y:F0}) — " +
                                  $"{(_latched ? "CLICK (latched)" : "drag end")}.");
            _latched = false;
        }
    }

    /// <summary>
    /// Requirement 4: in Menu2D the NON-dominant trigger switches dominance to that
    /// hand — only the dominant hand has a beam and clicks (per-hand Menu2D policy,
    /// HandsModule). Writes <c>[Hands] PrimaryHand</c> (BepInEx persists on set;
    /// HandsDriver reapplies the interactor masks via SettingChanged), so the card
    /// fan / wrist HUD side stays consistent. Returns true when a switch happened —
    /// the caller skips this frame so the freshly dominant hand's TriggerDown edge
    /// cannot fire an immediate accidental click.
    /// </summary>
    private bool TickHandednessSwitch()
    {
        if (VRModeStateMachine.CurrentMode != VRMode.Menu2D)
            return false;

        VRHand? primary = VRHands.Primary;
        VRHand? other = primary == VRHands.Left ? VRHands.Right : VRHands.Left;
        if (primary == null || other == null || !other.HasPose || !other.TriggerDown)
            return false;
        // Dev harness drives BOTH triggers from one key — a switch would flip-flop.
        if (other.IsSimulated)
            return false;

        if (_pressing)
        {
            _pressing = false;
            _latched = false;
            if (_mapPanGesture)
            {
                _stereo.EndMapPan();
                _mapPanGesture = false;
            }
            VirtualMouse.Release();
            EndScreenDrag();
        }

        string side = other.Side == HandSide.Left ? "Left" : "Right";
        Plugin.PrimaryHand.Value = side; // persisted (SaveOnConfigSet default true)
        other.SendHaptic(HapticPreset.ClickPulse);
        VRLog.Info("WorldUI", $"Handedness switch: {side} trigger pressed in Menu2D — dominant " +
                              $"hand is now {side} (laser + click move; fan/HUD follow on the other hand).");
        return true;
    }

    // ---- poke click (requirement 2) --------------------------------------------------------

    /// <summary>
    /// Fingertip poke on the flat screen = click at the poked RT position: quad-local
    /// hit → RT pixel → latched VirtualMouse warp+press on plane contact, release on
    /// withdraw. Both hands may poke (Menu2D grants Poke to both); the trigger-ray
    /// press and the poke press are mutually exclusive.
    /// </summary>
    private void TickPoke()
    {
        if (_quad == null || _rt == null)
            return;

        if (!WorldUIConfig.PokeClick.Value)
        {
            if (_pokePressing)
                EndPoke("poke click disabled");
            return;
        }

        Transform t = _quad.transform;

        if (_pokePressing)
        {
            VRHand? hand = _pokeHand;
            if (hand == null || !hand.HasPose)
            {
                EndPoke("hand lost");
                return;
            }

            // GRIP CHORD, re-checked every pending frame (user 2026-08, "alle buttons"): the flat
            // screen's poke click is a PHYSICAL fingertip press whose click lands on the WITHDRAWAL
            // (EndPoke with reason == null), i.e. exactly the shape the decision dock has — so a
            // press that armed under a held grip and is pulled back after the grip opened must NOT
            // click. Ending with a REASON is that guarantee: the virtual-mouse press is released,
            // any drag ends, and DirectClick is skipped. See
            // Hands.Interact.PokeInteractor.PressAllowed for the rule and why it is this condition.
            if (!PokePressAllowed(hand))
            {
                EndPoke("grip released (or the hand grabbed something) mid-press — no click");
                return;
            }

            float scale = hand.WorldScale;
            Vector3 tip = hand.Rig.IndexTip.position;
            float signed = Vector3.Dot(tip - t.position, t.forward); // viewer side < 0
            Vector3 local = t.InverseTransformPoint(tip);
            bool inRect = Mathf.Abs(local.x) <= 0.55f && Mathf.Abs(local.y) <= 0.55f;

            if (signed < -PokeReleaseMeters * scale || signed > PokeThroughMeters * scale || !inRect)
            {
                EndPoke(null); // normal withdraw (or slid off) → release = click/drag end
                return;
            }

            // Latch (same rationale as the trigger path): frozen at the press pixel
            // until the fingertip deliberately slides sideways.
            Vector3 onPlane = tip - t.forward * signed;
            if (_pokeLatched
                && (onPlane - _pokePressPoint).sqrMagnitude
                   > PokeDragUnlockMeters * PokeDragUnlockMeters * scale * scale)
            {
                _pokeLatched = false;
                // Same drag path as the trigger ray, and now the only one: uGUI drag events
                // (sliders/scrollbars follow). The virtual-mouse alternative went with
                // [WorldUI] ClickMode — it never moved a slider (test #7 button edges).
                BeginScreenDrag(_pokePressPixel);
                VRLog.Info("WorldUI", "FlatScreen poke: latch OPENED → drag (fingertip slid " +
                                      $">{PokeDragUnlockMeters * 1000f:F0} mm laterally).");
            }
            if (_pokeLatched)
            {
                // Same-pixel re-warp (see the trigger path): keeps currency reclaimed.
                VirtualMouse.WarpTo(_pokePressPixel);
            }
            else
            {
                float px = (Mathf.Clamp(local.x, -0.5f, 0.5f) + 0.5f) * _rt.width;
                float py = (Mathf.Clamp(local.y, -0.5f, 0.5f) + 0.5f) * _rt.height;
                var p = new Vector2(px, py);
                VirtualMouse.WarpTo(p);
                if (_screenDragActive)
                    UpdateScreenDrag(p);
            }
            return;
        }

        if (_pressing)
            return; // trigger-ray press owns the pointer

        TryBeginPoke(VRHands.Left, t);
        if (!_pokePressing)
            TryBeginPoke(VRHands.Right, t);
    }

    /// <summary>
    /// THE GRIP CHORD for the flat screen's fingertip click (user 2026-08: "Auch die
    /// Entscheidungsbuttons (so wie alle buttons) sollen nur auf pyhsisches Drücken reagieren wenn
    /// die Greiftaste gedrückt ist"). The 2D menu's buttons are buttons like any other, so the
    /// fingertip commits here under exactly the condition every other physical press in the mod
    /// uses — grip held, hand empty. The rule, its two halves and why HOVER stays ungated are
    /// documented once at <c>Hands.Interact.PokeInteractor.PressAllowed</c>; this is the flat
    /// screen's copy of the CONDITION, not of the reasoning.
    ///
    /// <para>The LASER is untouched: <c>TickPointer</c>'s trigger press is the primary way to work
    /// the 2D menu and stays grip-free, so nothing about the menu becomes unreachable.</para>
    /// </summary>
    private static bool PokePressAllowed(VRHand hand)
        => hand.GripPressed && hand.Grabber.Held == null;

    private void TryBeginPoke(VRHand? hand, Transform t)
    {
        // Respect the per-mode interactor matrix: only hands whose Poke interactor
        // is enabled may poke the screen.
        if (hand == null || !hand.HasPose || !hand.Poke.Enabled)
            return;

        float scale = hand.WorldScale;
        Vector3 tip = hand.Rig.IndexTip.position;
        float signed = Vector3.Dot(tip - t.position, t.forward); // viewer side < 0
        if (signed < -PokeContactMeters * scale || signed > PokeThroughMeters * scale)
            return; // not touching the plane / far behind it

        Vector3 local = t.InverseTransformPoint(tip);
        if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f)
            return;

        // GRIP CHORD (see PokePressAllowed): checked AFTER the contact + rect tests, so the log
        // below only speaks when the fingertip really is on the screen — and checked before
        // anything is latched or warped, so a grip-less touch leaves no state behind at all. The
        // press is NOT consumed: closing the grip with the fingertip already on the plane begins
        // the poke on the next frame, the same commit-time semantics the keycaps and the poked
        // canvases use.
        if (!PokePressAllowed(hand))
        {
            if (Time.unscaledTime >= _nextPokeGripLogAt)
            {
                _nextPokeGripLogAt = Time.unscaledTime + 1f;
                VRLog.Info("WorldUI", $"FlatScreen poke WITHHELD ({hand.Side}) — a physical fingertip " +
                                      "press commits only while the SAME hand holds the GRIP and " +
                                      $"carries nothing (grip {(hand.GripPressed ? "held" : "open")}, " +
                                      $"hand {(hand.Grabber.Held != null ? "carrying something" : "empty")}); " +
                                      "the trigger laser click is unaffected.");
            }
            return;
        }

        var pixel = new Vector2((local.x + 0.5f) * _rt!.width, (local.y + 0.5f) * _rt.height);
        _pokePressing = true;
        _pokeHand = hand;
        _pokeLatched = true; // always latched — see the trigger path ([WorldUI] ClickLatch is gone)
        _pokePressPoint = tip - t.forward * signed;
        _pokePressPixel = pixel;
        VirtualMouse.WarpTo(pixel);
        hand.SendHaptic(HapticPreset.ClickPulse);
        LogUnderPointer(pixel);
        VRLog.Info("WorldUI", $"FlatScreen poke: {hand.Side} fingertip PRESS at RT pixel " +
                              $"({pixel.x:F0},{pixel.y:F0}), latch={_pokeLatched}.");
    }

    private void EndPoke(string? reason)
    {
        _pokePressing = false;
        _pokeHand = null;
        // End any uGUI drag (endDrag + pointerUp); no-op for a plain latched tap.
        EndScreenDrag();
        // reason == null is the normal withdraw → deliver the click; any named reason
        // (hand lost, poke disabled) is an abort.
        if (_pokeLatched && reason == null)
            DirectClick(_pokePressPixel);
        VRLog.Info("WorldUI", $"FlatScreen poke: fingertip RELEASE — " +
                              $"{reason ?? (_pokeLatched ? "CLICK (latched)" : "drag end")}.");
        _pokeLatched = false;
    }

    private void HideReticle()
    {
        if (_pressing)
        {
            _pressing = false;
            _latched = false;
            EndScreenDrag();
            VRLog.Info("WorldUI", "FlatScreen pointer: press released (ray left the screen / pose lost).");
        }
    }

    // ---- direct click delivery (test #7) ----------------------------------------------------

    private static readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>
        s_raycastResults = new(16);

    /// <summary>
    /// Delivers a latched click directly through uGUI ExecuteEvents at the given RT
    /// pixel — the exact mechanism the game itself uses for programmatic clicks
    /// (BaseButtons.clickButton, UI-ARCH §5). Bypasses the input module entirely, so
    /// no frame-edge/pointer-currency quirk can swallow it. Modality is respected by
    /// construction: EventSystem.RaycastAll only returns hits from ENABLED
    /// GraphicRaycasters (UIManager.ToggleLockUI disables them to lock the UI).
    /// </summary>
    private void DirectClick(Vector2 pixel)
    {
        UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null)
        {
            VRLog.Warn("WorldUI", "DirectClick: no EventSystem — click dropped.");
            return;
        }

        var data = new UnityEngine.EventSystems.PointerEventData(es)
        {
            position = pixel,
            button = UnityEngine.EventSystems.PointerEventData.InputButton.Left,
            clickCount = 1,
            clickTime = Time.unscaledTime,
            eligibleForClick = true,
        };
        s_raycastResults.Clear();
        es.RaycastAll(data, s_raycastResults);
        if (s_raycastResults.Count == 0)
        {
            VRLog.Info("WorldUI", $"DirectClick at ({pixel.x:F0},{pixel.y:F0}): nothing under the pointer.");
            VRKeyboard.NoticeClick(null); // clicking into nothing still means "away from the keyboard"
            return;
        }

        UnityEngine.EventSystems.RaycastResult top = s_raycastResults[0];

        // The on-screen keyboard opens and closes on CLICKS, and this is the one place where a
        // click's target is known — the laser and the fingertip poke both arrive here. Told before
        // the click is delivered, so the keyboard is already up when the field processes it.
        VRKeyboard.NoticeClick(top.gameObject);

        data.pointerCurrentRaycast = data.pointerPressRaycast = top;

        GameObject? pressTarget = UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(
            top.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
        GameObject? clickTarget = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler
            <UnityEngine.EventSystems.IPointerClickHandler>(top.gameObject);
        data.pointerPress = pressTarget ?? clickTarget;

        if (data.pointerPress != null)
            UnityEngine.EventSystems.ExecuteEvents.Execute(
                data.pointerPress, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);

        if (clickTarget != null)
        {
            UnityEngine.EventSystems.ExecuteEvents.Execute(
                clickTarget, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            VRLog.Info("WorldUI", $"DirectClick at ({pixel.x:F0},{pixel.y:F0}) → clicked '{clickTarget.name}'.");
        }
        else
        {
            VRLog.Info("WorldUI", $"DirectClick at ({pixel.x:F0},{pixel.y:F0}): top hit " +
                                  $"'{top.gameObject.name}' has no IPointerClickHandler.");
        }
    }

    // ---- thumbstick scroll on the flat composite (user: "auch im Hauptmenü") ---------------

    /// <summary>Stick-Y deadzone (mirrors <c>Hands.Interact.RayUguiDriver.ScrollDeadzone</c>).</summary>
    private const float StickScrollDeadzone = 0.3f;

    /// <summary>
    /// Wheel NOTCHES per second at full deflection (mirrors
    /// <c>RayUguiDriver.ScrollNotchesPerSecond</c>), so a flat-screen list scrolls at exactly
    /// the felt speed of the converted world-space lists — each ScrollRect applies its own
    /// scrollSensitivity px/notch, just as for a real wheel.
    /// </summary>
    private const float StickScrollNotchesPerSecond = 40f;

    /// <summary>Distinct pointer id for the synthesized wheel (clear of mouse -1..-3, poke/laser, drag).</summary>
    private const int StickScrollPointerId = -121;

    /// <summary>Minimum spacing of the stick-scroll diagnostic line (per scroll target).</summary>
    private const float StickScrollLogSeconds = 2f;

    /// <summary>Reused wheel payload (no per-frame allocation once the first scroll happened).</summary>
    private UnityEngine.EventSystems.PointerEventData? _stickScrollData;
    /// <summary>EventSystem the payload was built for — a scene load replaces it, so the
    /// payload is rebuilt rather than left holding a destroyed one.</summary>
    private UnityEngine.EventSystems.EventSystem? _stickScrollEventSystem;
    private GameObject? _lastStickScrollTarget;
    private float _lastStickScrollLog = float.NegativeInfinity;
    private float _lastStickScrollMissLog = float.NegativeInfinity;

    /// <summary>
    /// Thumbstick scrolling for everything shown on the flat composite — the MAIN MENU and
    /// every unconverted window (user report: "scrolling works during a scenario but not in
    /// the main menu"). In a scenario the scrollable lists are converted world-space canvases
    /// driven by <c>Hands.Interact.RayUguiDriver.TickStickScroll</c>; the main menu is not
    /// converted at all — it is the screen-space game UI captured into this quad's
    /// RenderTexture — so that path never sees it and the stick was inert. This synthesizes
    /// the same mouse-wheel notch stream on the game's own UI, using the SAME
    /// EventSystem.RaycastAll-at-the-RT-pixel mechanism as <see cref="DirectClick"/> /
    /// <see cref="BeginScreenDrag"/>, and therefore inherits its modality guarantee: a locked
    /// UI (UIManager.ToggleLockUI disables every GraphicRaycaster) yields no hits and no events.
    ///
    /// Stick contention — nothing that legitimately owns the stick loses it:
    /// - X axis only: SnapTurn (engage |x| ≥ 0.7) and AoE rotation. This reads Y only.
    /// - Y axis on the CAMPAIGN MAP is the map zoom (<c>FlatScreenStereo.TickMapInput</c>), so
    ///   this stands down entirely while the map is active (and during a map pan).
    /// - While a trigger-drag session already moves a slider/scrollbar under the pointer the
    ///   stick stands down too, so one gesture owns the widget at a time.
    /// Unscaled time: menus pause the game clock.
    /// </summary>
    private void TickStickScroll(Vector2 pixel, VRHand hand, bool mapActive)
    {
        if (mapActive || _mapPanGesture || _screenDragActive)
            return;

        float y = hand.Thumbstick.y;
        if (Mathf.Abs(y) < StickScrollDeadzone)
            return;

        UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null)
            return;

        if (_stickScrollData == null || !ReferenceEquals(_stickScrollEventSystem, es))
        {
            _stickScrollEventSystem = es;
            _stickScrollData = new UnityEngine.EventSystems.PointerEventData(es)
            {
                pointerId = StickScrollPointerId,
            };
        }
        UnityEngine.EventSystems.PointerEventData data = _stickScrollData;
        data.position = pixel;
        s_raycastResults.Clear();
        es.RaycastAll(data, s_raycastResults);
        if (s_raycastResults.Count == 0)
            return;

        UnityEngine.EventSystems.RaycastResult top = s_raycastResults[0];
        data.pointerCurrentRaycast = top;
        GameObject? target = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler
            <UnityEngine.EventSystems.IScrollHandler>(top.gameObject);
        if (target == null)
        {
            // Nothing scrollable under the laser — the stick keeps its other meanings.
            // Throttled diagnostic: "I pushed the stick and nothing scrolled" is then
            // answerable from the log alone (which element the laser actually hit).
            if (Time.unscaledTime - _lastStickScrollMissLog >= StickScrollLogSeconds)
            {
                _lastStickScrollMissLog = Time.unscaledTime;
                VRLog.Info("WorldUI", $"Flat-menu stick scroll: nothing scrollable under the laser at RT " +
                                      $"pixel ({pixel.x:F0},{pixel.y:F0}) — top hit '{top.gameObject.name}' " +
                                      "has no IScrollHandler in its ancestors.");
            }
            return;
        }

        // Deadzone-normalized response: speed ramps from 0 at the deadzone edge to
        // StickScrollNotchesPerSecond at full deflection (identical to the world-space path).
        float response = (Mathf.Abs(y) - StickScrollDeadzone) / (1f - StickScrollDeadzone);
        float notches = Mathf.Sign(y) * response * StickScrollNotchesPerSecond * Time.unscaledDeltaTime;
        data.scrollDelta = new Vector2(0f, notches);
        UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(
            top.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.scrollHandler);
        data.scrollDelta = Vector2.zero; // never leak a stale wheel into a later event
        // SCROLL BEATS FLIGHT (Hands.Interact.UiScrollFocus). Mostly belt-and-braces here:
        // Flight already stands down in VRMode.Menu2D outright, so the only rig this line
        // protects is the flat-screen DEV PROXY, which does fly in Menu2D. Reported from the
        // delivery point only — this path resolves its scroll target AFTER the deadzone test
        // (the RaycastAll is deliberately not run on idle frames), so there is no honest hover
        // signal to publish before a push, and the grace window covers the rest.
        UiScrollFocus.NoteScrollDelivered(hand, target, nameof(FlatScreen));

        if (!ReferenceEquals(target, _lastStickScrollTarget)
            || Time.unscaledTime - _lastStickScrollLog >= StickScrollLogSeconds)
        {
            _lastStickScrollTarget = target;
            _lastStickScrollLog = Time.unscaledTime;
            VRLog.Info("WorldUI", $"Flat-menu stick scroll on '{target.name}' at RT pixel " +
                                  $"({pixel.x:F0},{pixel.y:F0}) — stick-Y {y:F2} → " +
                                  $"{notches:F3} wheel notches this frame (top hit '{top.gameObject.name}').");
        }
    }

    // ---- execute-mode drag delivery (flat-menu sliders / scrollbars / scroll-rects) --------

    /// <summary>
    /// Begin an ExecuteEvents drag session at the press pixel — the click latch has just
    /// opened into a deliberate drag. A uGUI <see cref="UnityEngine.UI.Slider"/> handle,
    /// scrollbar or scroll-rect moves ONLY through <c>IDragHandler.OnDrag</c>; the
    /// <see cref="DirectClick"/> down/up/click sets a value at the press pixel but can
    /// never fine-adjust it or reach an extreme the press pixel is not on (drag a volume
    /// slider to 0). Mirrors <see cref="Hands.Interact.UguiPointer"/> for the whole-screen
    /// composite: hits come from <c>EventSystem.RaycastAll</c> at the RT pixel (= the
    /// game's real screen pixel), exactly like <see cref="DirectClick"/>, so
    /// <c>pointerPressRaycast.module.eventCamera</c> (the UICamera) resolves the slider's
    /// local point correctly. pointerDown + <c>initializePotentialDrag</c> here;
    /// <see cref="UpdateScreenDrag"/> fires beginDrag/dragHandler as the pointer follows;
    /// <see cref="EndScreenDrag"/> fires endDrag + pointerUp on release.
    /// </summary>
    private void BeginScreenDrag(Vector2 pixel)
    {
        if (_screenDragActive)
            return;
        UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null)
        {
            VRLog.Warn("WorldUI", "Flat-menu drag: no EventSystem — drag dropped (slider cannot follow).");
            return;
        }

        var data = new UnityEngine.EventSystems.PointerEventData(es)
        {
            pointerId = ScreenDragPointerId,
            position = pixel,
            pressPosition = pixel,
            button = UnityEngine.EventSystems.PointerEventData.InputButton.Left,
            eligibleForClick = false,  // a deliberate drag is not a click
            useDragThreshold = false,  // VR: begin dragging on the first move, no pixel threshold
        };
        s_raycastResults.Clear();
        es.RaycastAll(data, s_raycastResults);
        if (s_raycastResults.Count == 0)
        {
            VRLog.Info("WorldUI", $"Flat-menu drag: nothing under the pointer at ({pixel.x:F0},{pixel.y:F0}) — no drag.");
            return;
        }

        UnityEngine.EventSystems.RaycastResult top = s_raycastResults[0];
        data.pointerCurrentRaycast = data.pointerPressRaycast = top;

        GameObject? pressTarget = UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(
            top.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
        _screenDragPress = pressTarget != null ? pressTarget : top.gameObject;
        data.pointerPress = _screenDragPress;

        _screenDragTarget = UnityEngine.EventSystems.ExecuteEvents.GetEventHandler
            <UnityEngine.EventSystems.IDragHandler>(top.gameObject);
        data.pointerDrag = _screenDragTarget;
        if (_screenDragTarget != null)
            UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(
                top.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.initializePotentialDrag);

        _screenDragData = data;
        _screenDragActive = true;
        _screenDragBegun = false;
        _screenDragLastPos = pixel;
    }

    /// <summary>
    /// Drive the active flat-menu drag as the pointer follows: update position + delta,
    /// fire beginDrag on the first move (ScrollRect wants it; Slider/Scrollbar have no
    /// IBeginDragHandler and simply ignore it) and dragHandler every tick thereafter.
    /// No-op when nothing draggable sits under the press.
    /// </summary>
    private void UpdateScreenDrag(Vector2 pixel)
    {
        if (!_screenDragActive || _screenDragData == null || _screenDragTarget == null)
        {
            _screenDragLastPos = pixel;
            return;
        }
        UnityEngine.EventSystems.PointerEventData data = _screenDragData;
        data.delta = pixel - _screenDragLastPos;
        data.position = pixel;
        _screenDragLastPos = pixel;

        if (!_screenDragBegun)
        {
            _screenDragBegun = true;
            data.dragging = true;
            UnityEngine.EventSystems.ExecuteEvents.Execute(
                _screenDragTarget, data, UnityEngine.EventSystems.ExecuteEvents.beginDragHandler);
            if (Time.unscaledTime - _lastScreenDragLog >= ScreenDragLogSeconds)
            {
                _lastScreenDragLog = Time.unscaledTime;
                VRLog.Info("WorldUI", $"Flat-menu drag BEGIN on '{_screenDragTarget.name}' at " +
                                      $"({pixel.x:F0},{pixel.y:F0}) — sliders/scrollbars now follow the " +
                                      "pointer via uGUI OnDrag (not just the click on the press pixel).");
            }
        }
        UnityEngine.EventSystems.ExecuteEvents.Execute(
            _screenDragTarget, data, UnityEngine.EventSystems.ExecuteEvents.dragHandler);
    }

    /// <summary>
    /// End the active flat-menu drag (endDrag + pointerUp at the last dispatched pixel);
    /// a safe no-op when none is active. A drag is never a click (it opened the latch), so
    /// no pointerClick is fired — matching the old virtual-mouse drag path (release only).
    /// </summary>
    private void EndScreenDrag()
    {
        if (!_screenDragActive)
            return;
        UnityEngine.EventSystems.PointerEventData? data = _screenDragData;
        if (data != null)
        {
            data.position = _screenDragLastPos;
            if (_screenDragPress != null)
                UnityEngine.EventSystems.ExecuteEvents.Execute(
                    _screenDragPress, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
            if (_screenDragBegun && _screenDragTarget != null)
                UnityEngine.EventSystems.ExecuteEvents.Execute(
                    _screenDragTarget, data, UnityEngine.EventSystems.ExecuteEvents.endDragHandler);
            data.dragging = false;
            data.pointerDrag = null;
            data.pointerPress = null;
        }
        if (_screenDragBegun && Time.unscaledTime - _lastScreenDragLog >= ScreenDragLogSeconds)
        {
            _lastScreenDragLog = Time.unscaledTime;
            VRLog.Info("WorldUI", $"Flat-menu drag END at ({_screenDragLastPos.x:F0},{_screenDragLastPos.y:F0}).");
        }
        _screenDragActive = false;
        _screenDragBegun = false;
        _screenDragPress = null;
        _screenDragTarget = null;
        _screenDragData = null;
    }

    /// <summary>
    /// Press-time diagnostic (all click modes): logs the top uGUI raycast hits under
    /// the press pixel, so a click that lands on the wrong element (or on nothing) is
    /// attributable from the log alone.
    /// </summary>
    private static void LogUnderPointer(Vector2 pixel)
    {
        UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null)
            return;
        var data = new UnityEngine.EventSystems.PointerEventData(es) { position = pixel };
        s_raycastResults.Clear();
        es.RaycastAll(data, s_raycastResults);
        if (s_raycastResults.Count == 0)
        {
            VRLog.Info("WorldUI", $"Under pointer ({pixel.x:F0},{pixel.y:F0}): nothing.");
            return;
        }
        int n = Mathf.Min(3, s_raycastResults.Count);
        var sb = new System.Text.StringBuilder(128);
        sb.Append($"Under pointer ({pixel.x:F0},{pixel.y:F0}): ");
        for (int i = 0; i < n; i++)
        {
            if (i > 0) sb.Append(" | ");
            GameObject go = s_raycastResults[i].gameObject;
            Canvas? root = go.GetComponentInParent<Canvas>();
            sb.Append($"'{go.name}'");
            if (root != null)
                sb.Append($" (canvas '{root.rootCanvas.name}')");
        }
        VRLog.Info("WorldUI", sb.ToString());
    }
}
