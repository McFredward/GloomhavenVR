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
/// Runtime caveats to validate on HMD/Windows (docs/TESTING-P2.md):
/// - A real hardware mouse also feeds the same pointer state; while the player moves
///   the physical mouse, the two fight for `Mouse.current`. Acceptable in VR (the
///   physical mouse is idle); Phase 3c can call InputManager.DisableAllMouses if not.
/// - Click() holds the press for ≥2 InputSystem updates so InControl's WasPressed
///   polling can't miss the edge; needs the WorldUI driver's Tick() running.
/// </summary>
internal static class VirtualMouse
{
    private static Mouse? _mouse;
    private static int _pendingReleaseFrame = -1;

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
        _mouse!.WarpCursorPosition(screenPos);
        InputState.Change(_mouse.position, screenPos);
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

    /// <summary>Deferred click release. Called every frame by the WorldUI driver.</summary>
    internal static void Tick()
    {
        if (_pendingReleaseFrame >= 0 && Time.frameCount >= _pendingReleaseFrame)
        {
            _pendingReleaseFrame = -1;
            Release();
        }
    }

    /// <summary>Drop our device reference (module shutdown; the game owns the device itself).</summary>
    internal static void Reset()
    {
        if (_pendingReleaseFrame >= 0)
        {
            _pendingReleaseFrame = -1;
            Release();
        }
        _mouse = null;
    }

    private static void SetLeftButton(bool pressed)
    {
        if (!EnsureCreated())
            return;
        _mouse!.CopyState(out MouseState state);
        InputState.Change(_mouse, state.WithButton(MouseButton.Left, pressed));
    }
}
