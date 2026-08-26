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
/// another device holds it (keep-alive; least invasive — no game patch).
///
/// LEVEL-TRIGGERED KEEP-ALIVE (I5 — hardware test #17 root cause: HMD doffed mid-
/// session, session unrecoverable). The I3 reclaim above is EDGE-triggered: it runs
/// only inside WarpTo/SetLeftButton, whose sole callers live in the FlatScreen
/// composite. With window-style modals every poke/laser click goes through
/// ExecuteEvents instead, so in steady state NOTHING drove the virtual mouse — and
/// when the headset standby made Unity's InputSystem re-enumerate devices and
/// promote the physical 'Mouse' to <c>Mouse.current</c>, no code path existed that
/// could ever take it back: the game read the frozen hardware cursor for the rest
/// of the session (IsPointerOverUI flicker, dead button edges, un-dismissable
/// modal). Fix: <see cref="Tick"/> re-asserts currency every
/// <see cref="KeepAliveIntervalFrames"/> frames whenever another device holds it,
/// re-adding our device first if the re-enumeration removed it. A physical mouse
/// that is ACTIVELY producing input (hardware updates within
/// <see cref="ThiefActiveGraceSeconds"/>) keeps currency — the deliberate desktop
/// fallback stays usable — but a stale device (the doff case) cannot hold it.
///
/// PRESENCE-GATED CURRENCY (I6 — hardware test #18: a currency FLIP-WAR between
/// 'Mouse' and 'ConsoleVirtualMouse' every ~2 log lines for whole sessions). The
/// I5 active-thief grace assumed hardware mouse input means deliberate desktop
/// use — but Virtual Desktop keeps injecting host mouse events while the user
/// plays in VR, so the physical 'Mouse' stayed permanently "fresh": every VD event
/// made it current (MakeCurrent on state-event processing), our 30-frame keep-
/// alive took it back, the next event stole it again — a fight the game observed
/// as pointer flapping. The HMD presence signal already computed by
/// <see cref="VRPresenceWatch"/> (userPresence feature / live head pose — the
/// heartbeat's own signals) resolves the ambiguity: while the HMD is WORN, nobody
/// can be using the desktop deliberately, so the virtual mouse ALWAYS wins — no
/// grace, no throttle, re-asserted every frame in <see cref="TickKeepAlive"/>. The
/// desktop-fallback grace applies only while the HMD is not worn. State
/// transitions (worn-policy flips, fight start, fight truly resolved after
/// <see cref="FightResolvedSeconds"/> of stable ownership) are logged ONCE each —
/// never per flip; the Mouse.current attribution log is rate-limited with a
/// suppressed-flip counter for the same reason.
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
///
/// MOUSE ERADICATION ROUND 2 (I7 — hardware report 2026-08-12, verbatim: "Die Maus
/// ist immer noch zu sehen auf dem Flatscreen und verursacht immer noch das manchmal
/// Tabs im Optionsmenu durch Kopfbewegungen gehighlighted werden (wie ein mouseover).
/// Das soll beides ganz verschwinden. Das passiert auch an anderer Stelle. Stelle
/// sicher, dass NUR der laser zu einem hovered führt (und eventuell physisch berühren
/// mit der hand, aber niemans mit Kopfbewegungen einfach so."). Three distinct causes,
/// three cuts:
///
/// <para>1. THE VISIBLE CURSOR IS THE OS HARDWARE CURSOR. Nothing in the game ever
/// sets <c>Cursor.visible = false</c> during play (decompiled sweep: only niche flows
/// — SelectInputDeviceBox gamepad prompt, BFX/RFX orbit cams — toggle it, and
/// <c>InputSystemUtilities.DisableMouseCursor</c> has no caller), AND every
/// <see cref="WarpTo"/> executes <c>Mouse.WarpCursorPosition</c>, whose
/// <c>WarpMousePositionCommand</c> moves the REAL OS cursor — so the white arrow
/// visibly rides the laser across the game window (= the desktop mirror the flat
/// screen shows). <see cref="TickHideHardwareCursor"/> pins <c>Cursor.visible</c>
/// false every frame while VR runs (game flows re-enable it; the pin wins) and
/// restores the original on VR stop — desktop non-VR play is untouched. The laser
/// reticle is the only pointer the player ever sees.</para>
///
/// <para>2. THE PARKED PIXEL IS A STANDING HOVER SOURCE. When the laser leaves the
/// flat screen, the virtual mouse simply STAYED at the last-warped pixel. The game's
/// input module re-raycasts that stale pixel EVERY frame
/// (<c>PointerInputModuleExtended.GetMousePointerEventData</c> reads
/// <c>InputSystemUtilities.GetMousePosition()</c> = our device state), so any content
/// that moves/scrolls/relayouts UNDER the pixel gets a genuine pointerEnter — hover
/// without any laser act. Direct position consumers (TextMeshProTooltip link hover,
/// HoverRegisterer world hover outside a board pick, AttackValueBreakdown's
/// follow-cursor anchor — the report's "an anderer Stelle") read the same stale
/// pixel. <see cref="TickParkOffScreen"/> parks the device OFF-SCREEN at
/// <see cref="ParkPixel"/> whenever no laser/fingertip warp drove it this frame:
/// RaycastAll at a negative pixel hits nothing and every screen-point test misses.
/// The game itself proves the tolerance: with <c>CursorLockMode.Locked</c> its own
/// module parks the pointer at (-1,-1) and ProcessMove exits any stale hover
/// (PointerInputModuleExtended.cs:279/326) — the exact code path a park exercises.
/// A pixel parked ON-screen at a fixed spot is NOT enough (round-1 note): whatever
/// slides under it still hovers.</para>
///
/// <para>3. Head-swept hover on WORLD-SPACE panels (the options-menu tabs) is cut by
/// <c>Patches.MouseWorldSurfaceCut</c> — re-applied 2026-08-12 after commit 8594ebd
/// accidentally reverted it (parallel worker, stale tree), which is why ModBuild 124
/// still showed the round-1 symptom.</para>
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

    // ---- I5 level-triggered keep-alive state (test #17) ---------------------------------

    /// <summary>Keep-alive attempt cadence while another device holds pointer currency.</summary>
    private const int KeepAliveIntervalFrames = 30;

    /// <summary>A thief with hardware input newer than this is in deliberate use — leave it alone.
    /// Applies ONLY while the HMD is not worn (I6, test #18): worn = no grace at all.</summary>
    private const double ThiefActiveGraceSeconds = 0.25;

    private static int _nextKeepAliveFrame;
    private static bool _keepAliveFighting;

    // ---- I6 presence-gated currency state (test #18) ------------------------------------

    /// <summary>A fight only counts as resolved after currency stayed ours this long.</summary>
    private const float FightResolvedSeconds = 3f;

    /// <summary>Minimum spacing of Mouse.current attribution log lines (flip-war dedup).</summary>
    private const float DeviceFlipLogSeconds = 5f;

    private static bool _wornPolicyKnown;
    private static bool _lastWornPolicy;
    private static float _lastContestedTime = float.NegativeInfinity;
    private static float _lastDeviceFlipLog = float.NegativeInfinity;
    private static int _suppressedFlips;

    // ---- physical-mouse suppression (accidental desktop-mouse hover, user report) ----------

    /// <summary>Physical mice WE disabled while VR runs, so we can re-enable exactly those on exit.
    /// Never contains our own virtual mouse. Rebuilt lazily; small (usually 0–1 entries).</summary>
    private static readonly System.Collections.Generic.List<Mouse> _disabledPhysicalMice = new();
    private static bool _mouseSuppressionActive;
    private static float _lastSuppressLog = float.NegativeInfinity;

    // ---- I7 mouse eradication round 2 (report 2026-08-12) --------------------------------

    /// <summary>Off-screen park position for the undriven virtual mouse. Negative on both
    /// axes: uGUI RaycastAll returns nothing, RectangleContainsScreenPoint and
    /// FindIntersectingLink miss, ScreenPointToRay points into empty space — the same
    /// tolerance envelope the game's own (-1,-1) locked-cursor park already exercises.</summary>
    internal static readonly Vector2 ParkPixel = new(-4096f, -4096f);

    /// <summary>Frames without a drive before parking. 2 covers tick-order jitter between the
    /// FlatScreen pointer tick and this tick without ever letting a live laser park.</summary>
    private const int ParkAfterFrames = 2;

    /// <summary>frameCount of the last REAL warp (laser/fingertip drive) — the seam that knows
    /// "the laser is currently driving the pointer". Parking itself does not count. -1 =
    /// never driven this session (NOT int.MinValue: frameCount - MinValue overflows negative
    /// and the boot-time park would silently never engage).</summary>
    private static int _lastDriveFrame = -1;

    private static bool _parked;
    private static bool _eradicationLogLatched;
    private static bool _cursorHidden;
    private static bool _cursorWasVisible = true;

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

        InputManager? manager = Singleton<InputManager>.Instance;

        if (_mouse != null && !_mouse.added)
        {
            // The game never removes its virtual mouse itself (verified: no
            // RemoveDevice call in decompiled GH.Runtime InputManager) — but an XR
            // runtime device re-enumeration (HMD standby, test #17) can. Re-add the
            // SAME instance so the game's InputManager._virtualMouse reference (and
            // its InputUser pairing) stays valid; the game's CreateVirtualMouse
            // cannot recover this state itself (it only checks _virtualMouse == null,
            // so a removed-but-referenced device would stay dead forever). Only if
            // the re-add fails is the device rebuilt from scratch below.
            try
            {
                InputSystem.AddDevice(_mouse);
                // Re-added device state buffers start blank — re-queue our shadow so
                // position/buttons match what we last told the InputSystem, and the
                // event processing re-claims pointer currency the hardware way.
                InputSystem.QueueStateEvent(_mouse, _shadow);
                VRLog.Warn("WorldUI", "VirtualMouse device had been removed from the InputSystem " +
                                      "(device re-enumeration?) — re-added the same device.");
            }
            catch (System.Exception ex)
            {
                VRLog.Warn("WorldUI", $"VirtualMouse device was removed and could not be re-added " +
                                      $"({ex.GetType().Name}: {ex.Message}) — recreating from scratch.");
                if (manager != null)
                    manager._virtualMouse = null; // publicized field: let CreateVirtualMouse rebuild
                _mouse = null;
            }
            if (IsAvailable)
                return true;
        }

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
        // I7: every caller of this method is a real aimed drive (FlatScreen laser/poke —
        // verified: no other call sites). Mark the drive so the park stands down, and leave
        // the parked state the moment a real aim resumes.
        _lastDriveFrame = Time.frameCount;
        _parked = false;
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
        // from the BepInEx log alone. Reference/bool compares only. Rate-limited to
        // one line per DeviceFlipLogSeconds (I6, test #18: a currency flip-war wrote
        // this per ~2 lines for a whole session); flips in between are counted and
        // reported with the next line instead.
        if (_mouse != null)
        {
            Mouse? current = Mouse.current;
            if (current != _lastCurrentSeen)
            {
                _lastCurrentSeen = current;
                if (Time.unscaledTime - _lastDeviceFlipLog >= DeviceFlipLogSeconds)
                {
                    VRLog.Info("WorldUI", $"Pointer device changed: Mouse.current is now " +
                                          $"'{(current != null ? current.name : "none")}'" +
                                          $"{(current == _mouse ? " (our virtual mouse)" : " (NOT our virtual mouse — game reads this one)")}" +
                                          $"{(_suppressedFlips > 0 ? $" — {_suppressedFlips} unlogged flip(s) since the last report" : "")}.");
                    _lastDeviceFlipLog = Time.unscaledTime;
                    _suppressedFlips = 0;
                }
                else
                {
                    _suppressedFlips++;
                }
            }

            bool gamepadMode = InputManager.GamePadInUse;
            if (!_gamepadModeKnown || gamepadMode != _lastGamepadMode)
            {
                _gamepadModeKnown = true;
                _lastGamepadMode = gamepadMode;
                VRLog.Info("WorldUI", $"Game input mode: GamePadInUse={gamepadMode}.");
            }
        }

        // I7: ensure the device exists EARLY in a VR session (not only on first laser
        // contact) — until it exists, physical-mouse suppression and the off-screen park
        // cannot engage, and the frozen desktop-mouse pixel is a standing boot-time hover
        // source. Guarded on the game's InputManager so early boot does not warn-spam.
        if (VRSession.IsRunning && _mouse == null && Singleton<InputManager>.Instance != null)
            EnsureCreated();

        TickHideHardwareCursor();
        TickKeepAlive();
        TickSuppressPhysicalMice();
        TickParkOffScreen();
    }

    /// <summary>
    /// I7 defect A (report 2026-08-12: "Die Maus ist immer noch zu sehen auf dem
    /// Flatscreen"): the visible cursor is the OS HARDWARE cursor — the game never hides
    /// it during play, and every <see cref="WarpTo"/> drags it across the game window via
    /// <c>WarpCursorPosition</c>. Pin it invisible every frame while VR runs (game flows
    /// like SelectInputDeviceBox re-enable it — the per-frame pin wins); restore the
    /// original visibility the moment VR stops, so desktop non-VR play is untouched.
    /// </summary>
    private static void TickHideHardwareCursor()
    {
        if (VRSession.IsRunning)
        {
            if (!_cursorHidden)
            {
                _cursorHidden = true;
                _cursorWasVisible = Cursor.visible;
            }
            if (Cursor.visible)
                Cursor.visible = false;
        }
        else if (_cursorHidden)
        {
            _cursorHidden = false;
            Cursor.visible = _cursorWasVisible;
            VRLog.Info("WorldUI", "Hardware OS cursor visibility restored (VR stopped) — " +
                                  $"Cursor.visible={_cursorWasVisible}.");
        }
    }

    /// <summary>
    /// I7 defect B (report 2026-08-12: hover "wie ein mouseover" without the laser, "auch
    /// an anderer Stelle"): when nothing drove the pointer this frame, park the virtual
    /// mouse OFF-SCREEN. The game's input module re-raycasts the pointer pixel EVERY frame,
    /// so a pixel left parked ON the screen keeps hovering whatever sits — or scrolls —
    /// under it; direct consumers (TextMeshProTooltip links, HoverRegisterer outside a
    /// board pick, AttackValueBreakdown's follow anchor) read the same stale pixel. At
    /// <see cref="ParkPixel"/> every one of them sees empty space, and the module exits any
    /// stale hover exactly like its own (-1,-1) locked-cursor park. Never parks mid-press
    /// (a held or just-queued button edge must land on its real pixel), and un-parks via
    /// the next real <see cref="WarpTo"/>. The rule this enforces: hover exists ONLY while
    /// the laser points at the surface (or a fingertip touches it) — never from head
    /// movement, never from content sliding under a dead pixel.
    /// </summary>
    private static void TickParkOffScreen()
    {
        if (!VRSession.IsRunning || _mouse == null || !_mouse.added)
            return;
        if (_parked || _leftPressed || _pendingReleaseFrame >= 0)
            return;
        if (_lastDriveFrame >= 0 && Time.frameCount - _lastDriveFrame <= ParkAfterFrames)
            return;

        _parked = true;
        _shadow.position = ParkPixel;
        _mouse.WarpCursorPosition(ParkPixel); // OS clamps to a screen corner — cursor is hidden (defect A)
        InputState.Change(_mouse.position, ParkPixel);
        InputSystem.QueueStateEvent(_mouse, _shadow);

        if (!_eradicationLogLatched)
        {
            _eradicationLogLatched = true;
            // Info, not Debug (BepInEx's default disk config drops Debug): the one latched
            // proof line for the next hardware log, naming BOTH neutralized sources.
            VRLog.Info("WorldUI", "MOUSE ERADICATION round 2 live: (A) the visible cursor was the OS " +
                                  "HARDWARE cursor — never hidden by the game and dragged along the laser " +
                                  "by every WarpCursorPosition — now pinned invisible while VR runs; " +
                                  "(B) the virtual mouse now PARKS OFF-SCREEN at " +
                                  $"({ParkPixel.x:F0},{ParkPixel.y:F0}) whenever no laser/fingertip warp " +
                                  "drove it this frame, so the game's per-frame mouse raycast and every " +
                                  "direct CursorPosition consumer see empty space — hover exists ONLY " +
                                  "under a live laser or physical poke. World-space panels are covered by " +
                                  "MouseWorldSurfaceCut (re-applied). Further parks are not logged (steady state).");
        }
    }

    /// <summary>
    /// User report: the physical desktop mouse can still hover/select map (and menu) elements in VR,
    /// causing accidental mouse-overs — its last desktop position keeps pointing at whatever the RT
    /// maps under it, and Virtual Desktop keeps that device artificially "fresh". The keep-alive
    /// re-claims Mouse.current, but that only fixes WHICH device is read, not that the physical one
    /// still emits position/hover when it briefly holds currency. The clean cure is to DISABLE the
    /// physical mouse device(s) in the InputSystem while VR runs — only the VR laser drives the
    /// virtual mouse then. We track exactly what we disabled and re-enable it when VR stops (or the
    /// toggle is turned off), and never touch our own virtual mouse. Re-enumeration (HMD standby) can
    /// re-enable a device — this runs every frame, so it simply re-disables it.
    /// </summary>
    private static void TickSuppressPhysicalMice()
    {
        // SuppressPhysicalMouse is a constant since the 2026-08-22 settings audit (off let the
        // stale desktop pointer click things behind your back) — the read stays so the one place
        // it is spelled out is the one place it can be reverted.
        bool want = WorldUIConfig.SuppressPhysicalMouse
                    && VRSession.IsRunning
                    && _mouse != null && _mouse.added; // never disable the ONLY mouse (would kill input)

        if (want)
        {
            var devices = InputSystem.devices;
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i] is not Mouse m)
                    continue;
                if (ReferenceEquals(m, _mouse) || !m.added || !m.enabled)
                    continue;
                InputSystem.DisableDevice(m);
                if (ReferenceEquals(Mouse.current, m) && _mouse!.added)
                    _mouse.MakeCurrent(); // don't leave a disabled device as the read source
                if (!_disabledPhysicalMice.Contains(m))
                    _disabledPhysicalMice.Add(m);
                if (Time.unscaledTime - _lastSuppressLog >= 5f)
                {
                    _lastSuppressLog = Time.unscaledTime;
                    VRLog.Info("WorldUI", $"Physical mouse '{m.name}' disabled while VR runs " +
                                          "(accidental desktop hover suppression) — only the VR laser " +
                                          "drives the pointer.");
                }
            }
            _mouseSuppressionActive = true;
        }
        else if (_mouseSuppressionActive)
        {
            // Policy turned off (VR stopped or toggle cleared): restore what we disabled.
            RestorePhysicalMice();
        }
    }

    /// <summary>Re-enable every physical mouse we disabled (called when VR stops / suppression is off).</summary>
    private static void RestorePhysicalMice()
    {
        for (int i = 0; i < _disabledPhysicalMice.Count; i++)
        {
            Mouse m = _disabledPhysicalMice[i];
            if (m != null && m.added && !m.enabled)
                InputSystem.EnableDevice(m);
        }
        if (_disabledPhysicalMice.Count > 0)
            VRLog.Info("WorldUI", $"Physical mouse suppression lifted — re-enabled " +
                                  $"{_disabledPhysicalMice.Count} device(s).");
        _disabledPhysicalMice.Clear();
        _mouseSuppressionActive = false;
    }

    /// <summary>
    /// Currency keep-alive, presence-gated (I5 test #17 + I6 test #18 — see class
    /// doc): whenever another device holds <c>Mouse.current</c> while nothing
    /// actively drives the pointer, take it back. While the HMD is WORN
    /// (<see cref="VRPresenceWatch.UserPresent"/>) the virtual mouse always
    /// wins — re-asserted every frame, no grace for the physical mouse (Virtual
    /// Desktop keeps it artificially "fresh"). While the HMD is NOT worn, the
    /// desktop fallback applies: throttled re-assert that defers to a physical
    /// mouse with fresh hardware input. Steady-state cost: one reference compare
    /// per frame; every log line fires on a state TRANSITION, never per flip — a
    /// fight only counts as resolved after <see cref="FightResolvedSeconds"/> of
    /// uncontested ownership, so a flip-war logs exactly one start line.
    /// </summary>
    private static void TickKeepAlive()
    {
        if (_mouse == null)
            return; // never created this session — nothing to keep alive

        bool worn = VRPresenceWatch.UserPresent;
        if (!_wornPolicyKnown || worn != _lastWornPolicy)
        {
            _wornPolicyKnown = true;
            _lastWornPolicy = worn;
            VRLog.Info("WorldUI", worn
                ? "VirtualMouse keep-alive: HMD worn — virtual mouse holds pointer currency " +
                  "unconditionally (physical-mouse grace suspended)."
                : "VirtualMouse keep-alive: HMD not worn — desktop fallback active (a physical " +
                  $"mouse with hardware input fresher than {ThiefActiveGraceSeconds:F2}s keeps currency).");
        }

        if (_mouse.added && Mouse.current == _mouse)
        {
            // Resolution is only declared (and logged) once ownership has been
            // stable for a while — in a flip-war this branch is re-entered every
            // other frame and must stay silent (I6).
            if (_keepAliveFighting && Time.unscaledTime - _lastContestedTime >= FightResolvedSeconds)
            {
                _keepAliveFighting = false;
                VRLog.Info("WorldUI", "VirtualMouse keep-alive: pointer currency is ours again " +
                                      $"(stable for {FightResolvedSeconds:F0}s; {_currencyReclaims} " +
                                      "reclaim(s) this session).");
            }
            return;
        }
        _lastContestedTime = Time.unscaledTime;

        Mouse? thief = Mouse.current;
        if (worn)
        {
            // HMD worn: ALWAYS win, immediately, every frame — nobody can be using
            // the desktop deliberately while wearing the headset (I6, test #18).
            if (!_keepAliveFighting)
            {
                _keepAliveFighting = true;
                VRLog.Info("WorldUI", "VirtualMouse keep-alive: pointer currency held by " +
                                      $"'{(thief != null ? thief.name : "none")}' while the HMD is worn " +
                                      $"(device removed={!_mouse.added}) — re-asserting every frame; " +
                                      "further flips are not logged until this resolves.");
            }
            Reassert();
            return;
        }

        if (Time.frameCount < _nextKeepAliveFrame)
            return;
        _nextKeepAliveFrame = Time.frameCount + KeepAliveIntervalFrames;

        // HMD not worn: deliberate physical-mouse use holds currency legitimately
        // (desktop fallback): fresh hardware input defers the reclaim; a stale
        // device (the doff re-enumeration promoted a frozen 'Mouse') gets no grace.
        if (thief != null && thief != _mouse && thief.added
            && InputState.currentTime - thief.lastUpdateTime < ThiefActiveGraceSeconds)
            return;

        if (!_keepAliveFighting)
        {
            _keepAliveFighting = true;
            VRLog.Info("WorldUI", "VirtualMouse keep-alive: pointer currency held by " +
                                  $"'{(thief != null ? thief.name : "none")}' while we are idle " +
                                  $"(device removed={!_mouse.added}) — re-asserting every " +
                                  $"{KeepAliveIntervalFrames} frames until it sticks.");
        }
        Reassert();
    }

    /// <summary>
    /// Re-assert the virtual mouse as <c>Mouse.current</c>, re-adding the device
    /// first if the InputSystem removed it. Returns true when the device exists and
    /// currency was (re)claimed.
    /// </summary>
    private static bool Reassert()
    {
        if (!EnsureCreated())
            return false; // logs its own reason (InputManager not ready / re-add failed)
        if (Mouse.current == _mouse)
            return true;
        _mouse!.MakeCurrent();
        _currencyReclaims++;
        // Queued state event: the next input update processes it as a real device
        // event, which re-claims currency the hardware way (decompiled
        // InputManager.cs:2312-2322) — the direct MakeCurrent alone would lose to
        // any hardware event already queued this frame. Same state as last written:
        // no spurious button edge (previous-frame state is already equal).
        InputSystem.QueueStateEvent(_mouse, _shadow);
        return true;
    }

    /// <summary>
    /// Presence-regained recovery entry (test #17): immediately re-add/re-claim,
    /// bypassing the keep-alive throttle and the active-thief grace. Returns a short
    /// description of what was recovered for the "[Core] Session resumed" report, or
    /// null when nothing was wrong (or the bridge is not in use this session).
    /// </summary>
    internal static string? ForceReassert(string reason)
    {
        if (_mouse == null)
            return null; // never created this session — nothing to recover
        bool wasAdded = _mouse.added;
        bool wasCurrent = wasAdded && Mouse.current == _mouse;
        if (wasAdded && wasCurrent)
            return null;
        _nextKeepAliveFrame = 0;
        if (!Reassert())
            return null;
        VRLog.Info("WorldUI", $"VirtualMouse: forced re-assert ({reason}) — " +
                              $"device re-added={!wasAdded}, currency reclaimed={!wasCurrent}.");
        return wasAdded ? "virtual-mouse pointer currency re-asserted"
                        : "virtual-mouse device re-added + currency re-asserted";
    }

    /// <summary>Drop our device reference (module shutdown; the game owns the device itself).</summary>
    internal static void Reset()
    {
        if (_pendingReleaseFrame >= 0 || _leftPressed)
        {
            _pendingReleaseFrame = -1;
            Release();
        }
        if (_cursorHidden)
        {
            _cursorHidden = false;
            Cursor.visible = _cursorWasVisible; // module shutdown: never leave the OS cursor hidden
        }
        _parked = false;
        _lastDriveFrame = -1;
        _mouse = null;
        _lastCurrentSeen = null;
        _leftPressed = false;
        _shadow = default;
        _gamepadModeKnown = false;
        _currencyReclaims = 0;
        _lastReclaimLog = float.NegativeInfinity;
        _nextKeepAliveFrame = 0;
        _keepAliveFighting = false;
        _wornPolicyKnown = false;
        _lastContestedTime = float.NegativeInfinity;
        _lastDeviceFlipLog = float.NegativeInfinity;
        _suppressedFlips = 0;
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
