using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Bridge to the game's own console virtual mouse (FROZEN Phase-2 API) — the seed of
/// the WorldUI module. A hand ray hitting the floating 2D screen converts its hit UV
/// to screen pixels and drives this bridge; everything downstream (InControl input
/// module, ClickTracker, IsPointerOverUI, tooltips) keeps working with ZERO
/// InputModule patches (UI-ARCH §4.4 strategy 1).
///
/// Verified against the REAL assemblies with ilspycmd (2026-07-15):
/// <code>
///   // GH.Runtime.dll, InputManager : Singleton&lt;InputManager&gt; (global namespace)
///   public void CreateVirtualMouse()          // INSTANCE method (PATCH-TARGETS corr. #6);
///       // adds InputSystem.AddDevice&lt;Mouse&gt;("ConsoleVirtualMouse"), pairs it,
///       // MakeCurrent(), warps to screen center. Idempotent (checks _virtualMouse == null).
///   public Mouse VirtualMouse =&gt; _virtualMouse;
///   // Unity.InputSystem.dll (1.3.0)
///   public void Mouse.WarpCursorPosition(Vector2 position)
///   public static void InputState.Change&lt;TState&gt;(InputControl control, TState state, ...)
///   public static void InputControlExtensions.CopyState&lt;TState&gt;(this InputDevice device, out TState state)
///   public MouseState MouseState.WithButton(MouseButton button, bool state = true)
/// </code>
///
/// There is a SECOND, game-level virtual mouse (<c>Utilities.VirtualMouseUtilities</c>,
/// Utilities.dll — referenced by this project for Phase 3c): once registered it reroutes
/// <c>InputSystemUtilities.GetMousePosition()</c> and thus <c>InputManager.CursorPosition</c>,
/// but NOT the InControl uGUI module (BOARD-INPUT §5 caveat). This bridge drives the
/// InputSystem device, which feeds both paths — the right default.
///
/// POINTER CURRENCY (I3 root cause, hardware test #5 — hover/click died mid-session
/// while the game stayed healthy): every game-side pointer consumer reads
/// <c>Mouse.current</c>, not a specific device —
/// <code>
///   // decompiled/InControl/InControl/UnityMouseProvider.cs:40-52 (Update reads
///   //   Mouse.current for position/buttons → InControlInputModule.MouseHasMoved)
///   // decompiled/Utilities/Utilities/InputSystemUtilities.cs:174-187
///   //   (GetMousePosition → Mouse.current.position) and :96-146 (buttons)
///   // decompiled/InControl/InControl/PointerInputModuleExtended.cs:269-307
///   //   (GetMousePointerEventData → InputSystemUtilities.GetMousePosition)
/// </code>
/// and `Mouse.current` is ONLY re-assigned when a queued hardware state EVENT is
/// processed or a device is added/removed —
/// <code>
///   // decompiled/Unity.InputSystem/UnityEngine.InputSystem/InputManager.cs:2321
///   //   (state-event processing → inputDevice.MakeCurrent()), :1127 (device add),
///   //   :1246 (device removal promotes another device of the same type)
///   // decompiled/Unity.InputSystem/UnityEngine.InputSystem.LowLevel/InputState.cs:80-95
///   //   (InputState.Change → InputManager.UpdateState DIRECT write — no MakeCurrent)
/// </code>
/// This bridge writes the virtual device via <c>InputState.Change</c>, which never
/// (re)claims currency. One physical-mouse jitter therefore made the HARDWARE mouse
/// current, and every warp/press on the virtual device became invisible to the game
/// — hover dead, no freeze, exactly the test-#5 symptom. Fix: <see cref="WarpTo"/> /
/// <see cref="SetLeftButton"/> re-claim currency via <c>MakeCurrent()</c> whenever
/// another device holds it (keep-alive; least invasive — no game patch). While the
/// VR ray is OFF the screen we deliberately do not re-claim, so the physical mouse
/// keeps working as the desktop fallback.
///
/// FRAME-EDGE DELIVERY (I4 — hardware test #6 root cause: hover alive, warps alive,
/// ZERO clicks). The game's uGUI click path polls single-frame press EDGES on
/// <c>Mouse.current</c>:
/// <code>
///   // decompiled/InControl/InControl/PointerInputModuleExtended.cs:244-262
///   //   StateForMouseButton → InputSystemUtilities.GetMouseButtionDown/Up
///   // decompiled/Utilities/Utilities/InputSystemUtilities.cs:96-118 (Down) /
///   //   :122-144 (Up): Mouse.current.leftButton.wasPressedThisFrame /
///   //   wasReleasedThisFrame — NOT the persistent isPressed
///   // decompiled/Unity.InputSystem/UnityEngine.InputSystem.Controls/ButtonControl.cs:29-51
///   //   wasPressedThisFrame == device.wasUpdatedThisFrame
///   //     && pressed(now) && !pressed(previous update step)
///   // decompiled/Unity.InputSystem/UnityEngine.InputSystem/InputDevice.cs:113
///   //   wasUpdatedThisFrame == (m_CurrentUpdateStepCount == InputUpdate.s_UpdateStepCount)
/// </code>
/// A press written mid-frame via <c>InputState.Change</c> (our Update) only satisfies
/// those three conditions for code running AFTER the write in the SAME frame:
/// <code>
///   // decompiled/Unity.InputSystem/UnityEngine.InputSystem/InputManager.cs:2549 +
///   //   :2570-2579 — UpdateState flips the device's front/back state buffers lazily
///   //   on its FIRST write per update step and only then stamps
///   //   m_CurrentUpdateStepCount = InputUpdate.s_UpdateStepCount.
/// </code>
/// If the game's EventSystem polls BEFORE our Update in frame N, it sees the stale
/// step count (wasUpdatedThisFrame == false → edge invisible); in frame N+1 our first
/// write flips the buffers so "previous frame" already contains the pressed state
/// (edge gone). Same-order MonoBehaviour Update order is unspecified in Unity, and on
/// the test-#6 rig the EventSystem won: presses/releases were PERMANENTLY invisible
/// to uGUI while position (persistent state, no edge needed) kept hover alive —
/// exactly the observed symptom. Fix: every write is ALSO queued as a real state
/// EVENT (<c>InputSystem.QueueStateEvent</c>, decompiled InputSystem.cs:714), which
/// the next frame's InputSystem update processes BEFORE any MonoBehaviour Update —
/// hardware-identical semantics: the edge is visible to every consumer for that whole
/// frame regardless of script order, and state-event processing re-claims pointer
/// currency for free (InputManager.cs:2312-2322 → inputDevice.MakeCurrent()). The
/// immediate InputState.Change write is kept so favorable orderings see zero latency;
/// the queued event re-asserting the same state one frame later produces no second
/// edge (previous-frame state is already equal).
///
/// Runtime caveats to validate on HMD/Windows (docs/TESTING-P2.md):
/// - Click() holds the press for ≥2 InputSystem updates so InControl's WasPressed
///   polling can't miss the edge; needs the WorldUI driver's Tick() running.
/// </summary>
internal static class VirtualMouse
{
    private static Mouse? _mouse;
    private static int _pendingReleaseFrame = -1;

    /// <summary>
    /// Shadow of the state we last queued (I4): queued events are only processed at
    /// the NEXT frame's input update, so <c>CopyState</c> from the live device could
    /// miss a button we queued this frame — the shadow is the single source of truth
    /// for what we told the InputSystem. Struct field: no allocations.
    /// </summary>
    private static MouseState _shadow;

    // ---- I3 diagnostics + watchdog state ------------------------------------------------

    /// <summary>Force-release a held left button after this long without pointer movement.</summary>
    private const float StuckPressSeconds = 5f;

    /// <summary>Minimum pixel movement (squared) that counts as pointer activity.</summary>
    private const float ActivityEpsilonSq = 1f;

    private static bool _leftPressed;
    private static float _lastActivityTime;
    private static Vector2 _lastWarpPos;
    private static Mouse? _lastCurrentSeen;
    private static int _currencyReclaims;
    private static float _lastReclaimLog = float.NegativeInfinity;
    private static bool _lastGamepadMode;
    private static bool _gamepadModeKnown;

    /// <summary>True once the virtual mouse device exists and is usable.</summary>
    public static bool IsAvailable => _mouse != null && _mouse.added;

    /// <summary>
    /// Create (or adopt) the game's virtual mouse. Safe to call repeatedly.
    /// Returns false when InputManager does not exist yet (very early boot).
    /// </summary>
    public static bool EnsureCreated()
    {
        if (IsAvailable)
            return true;

        if (_mouse != null && !_mouse.added)
        {
            // The game never removes its virtual mouse itself (verified: no
            // RemoveDevice call in decompiled GH.Runtime InputManager), but be loud
            // if anything else does — CreateVirtualMouse re-adds it below.
            VRLog.Warn("WorldUI", "VirtualMouse device was removed from the InputSystem — recreating.");
            _mouse = null;
        }

        InputManager? manager = Singleton<InputManager>.Instance;
        if (manager == null)
        {
            VRLog.Warn("WorldUI", "VirtualMouse: InputManager singleton not ready.");
            return false;
        }

        // Instance method (verified); idempotent inside the game code.
        manager.CreateVirtualMouse();
        _mouse = manager.VirtualMouse;

        if (_mouse == null)
        {
            VRLog.Warn("WorldUI", "VirtualMouse: CreateVirtualMouse did not produce a device.");
            return false;
        }

        VRLog.Info("WorldUI", $"VirtualMouse ready ('{_mouse.name}').");
        return true;
    }

    /// <summary>Move the pointer to a screen position (pixels, bottom-left origin).</summary>
    public static void WarpTo(Vector2 screenPos)
    {
        if (!EnsureCreated())
            return;
        ReclaimCurrency();
        if ((screenPos - _lastWarpPos).sqrMagnitude > ActivityEpsilonSq)
        {
            _lastWarpPos = screenPos;
            _lastActivityTime = Time.unscaledTime;
        }
        _shadow.position = screenPos;
        _mouse!.WarpCursorPosition(screenPos);
        // Same-frame half (pollers that run after us) + next-frame half (queued state
        // event, edge-safe for pollers that run before us — I4, see class doc).
        InputState.Change(_mouse.position, screenPos);
        InputSystem.QueueStateEvent(_mouse, _shadow);
    }

    /// <summary>
    /// Keep-alive for pointer ownership (I3 — see class doc): if another mouse device
    /// stole <c>Mouse.current</c> (hardware jitter processes a state event →
    /// MakeCurrent, decompiled Unity.InputSystem InputManager.cs:2321), take it back.
    /// Called only while we actively drive the pointer, so an intentionally used
    /// physical mouse still works whenever the VR ray is off the screen.
    /// Reference compare per call — no allocations; logging rate-limited.
    /// </summary>
    private static void ReclaimCurrency()
    {
        if (Mouse.current == _mouse)
            return;
        Mouse? thief = Mouse.current;
        _mouse!.MakeCurrent();
        _currencyReclaims++;
        if (Time.unscaledTime - _lastReclaimLog >= 5f)
        {
            _lastReclaimLog = Time.unscaledTime;
            VRLog.Info("WorldUI", $"VirtualMouse: pointer currency was held by " +
                                  $"'{(thief != null ? thief.name : "none")}' — reclaimed via MakeCurrent " +
                                  $"({_currencyReclaims} reclaim(s) this session).");
        }
    }

    /// <summary>Press the left button (state persists until <see cref="Release"/>).</summary>
    public static void Press() => SetLeftButton(true);

    /// <summary>Release the left button.</summary>
    public static void Release() => SetLeftButton(false);

    /// <summary>
    /// Full click: optional warp, press now, auto-release two frames later (via
    /// <see cref="Tick"/>) so both event-driven and polled consumers see the edge.
    /// </summary>
    public static void Click(Vector2? screenPos = null)
    {
        if (screenPos.HasValue)
            WarpTo(screenPos.Value);
        Press();
        _pendingReleaseFrame = Time.frameCount + 2;
    }

    /// <summary>Start a drag at a screen position (press and keep held; move via WarpTo).</summary>
    public static void BeginDrag(Vector2 screenPos)
    {
        WarpTo(screenPos);
        Press();
    }

    /// <summary>Finish a drag at a screen position.</summary>
    public static void EndDrag(Vector2 screenPos)
    {
        WarpTo(screenPos);
        Release();
    }

    /// <summary>
    /// Per-frame service (WorldUI driver): deferred click release, the stuck-press
    /// watchdog and the I3 attribution diagnostics. Allocation-free between events.
    /// </summary>
    internal static void Tick()
    {
        if (_pendingReleaseFrame >= 0 && Time.frameCount >= _pendingReleaseFrame)
        {
            _pendingReleaseFrame = -1;
            Release();
        }

        // Stuck-press watchdog (I3 hardening): if OUR synthesized left button has
        // been held without any pointer movement for too long, force the release —
        // a press without its matching release keeps uGUI in drag state and kills
        // hover globally (PointerInputModuleExtended.ProcessDrag suppresses enter/
        // exit while dragging). Legit drags always move the pointer.
        if (_leftPressed && Time.unscaledTime - _lastActivityTime > StuckPressSeconds)
        {
            VRLog.Warn("WorldUI", $"VirtualMouse watchdog: left button held >{StuckPressSeconds:F0}s " +
                                  "without pointer movement — forcing release (stuck synthesized press).");
            Release();
        }

        // Attribution diagnostics: log pointer-ownership changes and the game's
        // input-mode flips so a recurrence of test-#5 input death is explainable
        // from the BepInEx log alone. Reference/bool compares only.
        if (_mouse != null)
        {
            Mouse? current = Mouse.current;
            if (current != _lastCurrentSeen)
            {
                _lastCurrentSeen = current;
                VRLog.Info("WorldUI", $"Pointer device changed: Mouse.current is now " +
                                      $"'{(current != null ? current.name : "none")}'" +
                                      $"{(current == _mouse ? " (our virtual mouse)" : " (NOT our virtual mouse — game reads this one)")}.");
            }

            bool gamepadMode = InputManager.GamePadInUse;
            if (!_gamepadModeKnown || gamepadMode != _lastGamepadMode)
            {
                _gamepadModeKnown = true;
                _lastGamepadMode = gamepadMode;
                VRLog.Info("WorldUI", $"Game input mode: GamePadInUse={gamepadMode}.");
            }
        }
    }

    /// <summary>Drop our device reference (module shutdown; the game owns the device itself).</summary>
    internal static void Reset()
    {
        if (_pendingReleaseFrame >= 0 || _leftPressed)
        {
            _pendingReleaseFrame = -1;
            Release();
        }
        _mouse = null;
        _lastCurrentSeen = null;
        _leftPressed = false;
        _shadow = default;
        _gamepadModeKnown = false;
        _currencyReclaims = 0;
        _lastReclaimLog = float.NegativeInfinity;
    }

    private static void SetLeftButton(bool pressed)
    {
        if (!EnsureCreated())
            return;
        ReclaimCurrency();
        if (pressed != _leftPressed)
        {
            _leftPressed = pressed;
            if (pressed)
                _lastActivityTime = Time.unscaledTime; // arm the stuck-press watchdog
            // Info (not Debug — BepInEx filters Debug from the disk log by default,
            // which made test #6 look like presses never fired): one line per edge,
            // with the screen position the edge lands on.
            VRLog.Info("WorldUI", $"VirtualMouse: left button {(pressed ? "PRESSED" : "RELEASED")} " +
                                  $"at ({_shadow.position.x:F0},{_shadow.position.y:F0}).");
        }
        _shadow = _shadow.WithButton(MouseButton.Left, pressed);
        // Same-frame half + next-frame half (queued state event) — I4, see class doc.
        _mouse!.CopyState(out MouseState state);
        InputState.Change(_mouse, state.WithButton(MouseButton.Left, pressed));
        InputSystem.QueueStateEvent(_mouse, _shadow);
    }
}
