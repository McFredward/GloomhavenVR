using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// CURRENTLY INERT BY DESIGN — read this before the description below. <c>_controls</c> is
/// <c>Array.Empty&lt;DockedControl&gt;()</c> and the four <c>*Docked</c>/<c>*Visible</c>
/// constants are hard-coded <c>false</c>; see the constructor for the per-control reason each
/// one was pulled (all three came down to the same occlusion bug: docked flat on the opaque
/// board the widget reported <c>alpha ≈ 1</c> while its pixels never reached the headset).
/// Nothing in this class docks anything today, and every turn-flow control is mod-drawn.
/// The mechanism below is RETAINED as the only implementation of the native-widget dock and as
/// a config-reachable alternative behind <c>[WorldUI] TrayNativeControls</c> (default false);
/// the four constants are also what other modules read to decide whether to draw their
/// mod-drawn twin, so removing them silently hides board buttons. Removing any of this is a
/// user decision (Tier 3), not a cleanup. Everything from here on describes the mechanism as
/// designed, i.e. what would happen if it were re-enabled — NOT what runs.
///
/// TRAY NATIVE CONTROLS (test #23 item 4): docks the game's REAL persistent
/// turn-flow widgets onto the control board so they carry the familiar in-game
/// style, replacing the mod-drawn CONFIRM/UNDO board buttons (<see cref="PlayTray"/>)
/// and the short-rest token (<see cref="RestControls"/>). Reuses EXACTLY the
/// DecisionDock docking mechanism (<see cref="DecisionDockSurface"/>): the widget's
/// RectTransform is moved onto a world-space host via <see cref="CanvasConversion"/>
/// (native sprite/label/enable-state ride along), the host pose-follows a tray
/// mount, and poke AND laser click the REAL uGUI button through the standard
/// conversion registration (UguiPokeSurfaces + RayUguiDriver). Every dock is
/// reversible — <see cref="CanvasConversion.Release"/> restores the widget to its
/// exact 2D home — and change-deduped in the log.
///
/// WIDGETS (decompiled evidence in <see cref="CardsGameApi"/>):
/// - CONTINUE / CONFIRM / "Fortfahren" / end-selection = <c>ReadyButton</c>
///   (Choreographer.readyButton) — the single confirm entry of the scenario loop
///   (16 EButtonState values incl. EREADYBUTTONCONTINUE). Docks at the tray's old
///   CONFIRM column position.
/// - UNDO / "Rückgängig machen" = <c>UndoButton</c> (Choreographer.m_UndoButton).
///   Docks at the old UNDO column position.
/// - SHORT REST / "Kurze Rast" = the active hand's <c>ShortRest</c> widget
///   (CardsHandUI.shortRest). Docks at the rest zone's short-rest anchor.
///
/// Ready and Undo are BARE HUD widgets (own CanvasGroup, self-SetActive; NO
/// UIWindow), so unlike DecisionDock there is no window remainder to suppress — a
/// dock is pose-follow the isolated widget subtree, nothing more. ShortRest lives
/// under the card-hand HUD (CardsHandManager.window), but converting it out of that
/// subtree makes it render on our host regardless of the hand suppression, exactly
/// as DecisionDock's docked row does.
///
/// NO NATIVE "Lange Rast": long rest has no discrete uGUI button — it is chosen by
/// selecting the long-rest pseudo-card in the fan and committed via Continue
/// (LongRestConfirmationButton wraps a card action, only in the improved-rest flow).
/// The long-rest token therefore STAYS mod-drawn in <see cref="RestControls"/>.
///
/// TIMING &amp; LIFECYCLE: level-triggered on the game's own show state — a control
/// docks only while its widget is active + visible (and, for short rest, in the
/// selection phase) and undocks the moment the game hides it (ReadyButton alpha→0 /
/// SetActive(false), the hand rebuild that destroys ShortRest, phase change). If the
/// widget cannot be docked (feature off, no tray, widget hidden) the mod-drawn
/// counterpart shows instead — never a missing control.
///
/// TEST #19 GUARD: the accidental-CONFIRM window after a slot drop/pluck
/// (<see cref="PlayTray.ConfirmGuardRemaining"/>) applies to the docked Continue
/// button too — its host raycaster is disabled while the guard runs, so a poke/laser
/// right after handling cards cannot fire it (the exact accident the mod CONFIRM's
/// ActivationGuard prevented). The docked buttons also respect the mirrored UI lock
/// (<see cref="CanvasConversion.IsLockedNow"/>) like every converted host.
/// </summary>
internal sealed class TrayControlDockSurface
{
    /// <summary>Live instance for the static docked-state queries (single instance per driver).</summary>
    internal static TrayControlDockSurface? Instance { get; private set; }

    private readonly DockedControl[] _controls;

    public TrayControlDockSurface()
    {
        Instance = this;
        // No native controls dock on the tray any more — every turn-flow control is now the
        // mod-drawn, depth-correct board button (Confirm/Undo on the right pads, short/long
        // rest discs in the notches). Reasons, in order they were pulled:
        //
        // - Short rest (item 1): the native "Kurze Rast" widget docked/undocked as the game
        //   toggled its active/visible state and flickered against the mod round disc. The
        //   round disc is now the SOLE short-rest control (RestControls.TickStatus). ShortRestDocked = false.
        // - Continue/Ready (item 2): docked flat on the opaque board its pixels never reached
        //   the headset (occluded) yet the game reported CanvasGroup.alpha≈1, so an alpha check
        //   could not tell "docked+shown" from "docked+invisible" and the mod Confirm stayed
        //   hidden with nothing visible while the docked raycaster stayed live. ContinueDocked = false.
        // - Undo (this pass): SAME occlusion bug — the docked UndoButton read alpha≈1 while
        //   occluded, so the mod Undo was hidden-but-pressable (invisible). The Undo DockedControl
        //   is removed so UndoDocked is permanently false; PlayTray always shows the depth-correct
        //   mod Undo, which appears exactly when CardsGameApi.CanUndo() (no visibility gate).
        //
        // With every control mod-drawn there is nothing left to dock, so the control set is empty.
        _controls = System.Array.Empty<DockedControl>();
    }

    /// <summary>
    /// Item 2 (test #23): the native Continue/ReadyButton no longer docks (see the
    /// constructor) — always false. The mod-drawn Confirm is the sole always-visible
    /// confirm control (docked flat on the opaque board it was occluded yet reported
    /// alpha≈1, so an alpha check could never detect the invisibility).
    /// </summary>
    internal static bool ContinueDocked => false;

    /// <summary>Item 2: native Continue no longer docks — nothing native to render; always false.</summary>
    internal static bool ContinueVisible => false;

    /// <summary>
    /// The native Undo (UndoButton) no longer docks (see the constructor) — always false.
    /// The mod-drawn Undo is the sole always-visible undo control (docked flat on the opaque
    /// board it read alpha≈1 yet was occluded, so it was invisible-but-pressable).
    /// </summary>
    internal static bool UndoDocked => false;

    /// <summary>Item 1: native short-rest no longer docks (see the constructor) — always false.</summary>
    internal static bool ShortRestDocked => false;

    /// <summary>
    /// Item 7: is the converted widget behind <paramref name="panel"/> actually rendering?
    /// The ReadyButton (and the other docked HUD widgets) carry their own
    /// <see cref="CanvasGroup"/>; the game drives its alpha to 0 to hide the control while
    /// leaving it active/interactable. A docked host whose widget alpha is ~0 is an
    /// invisible click-catcher — its raycaster is stood down (see <see cref="Place"/>) and,
    /// for Continue, the mod-drawn twin takes over. No CanvasGroup = treat as visible.
    /// </summary>
    private static bool HostRendering(ConvertedPanel panel)
    {
        if (panel.Target == null)
            return false;
        var cg = panel.Target.GetComponent<CanvasGroup>();
        return cg == null || cg.alpha > 0.01f;
    }

    /// <summary>
    /// Feature gate: config on, conversion live, in a scenario, a control board
    /// exists to dock onto, and the manual 2D screen is not forcing the full
    /// composite (a docked widget would be missing from it — the DecisionDock rule).
    /// </summary>
    private static bool FeatureEnabled =>
        WorldUIConfig.TrayNativeControls.Value
        && WorldUIConfig.ConversionActive
        && !FlatScreen.ManualScreenActive
        && Choreographer.s_Choreographer != null
        && PlayTray.Current != null;

    /// <summary>
    /// Hysteresis window (test #25 item 1a): a docked native control survives this long
    /// of continuous <c>FindWidget()==null</c> before releasing, so a one-frame game-
    /// side hide/rebuild does not flicker the dock against the mod-drawn button.
    /// </summary>
    private const float HoldSeconds = 0.5f;

    public void Tick()
    {
        bool feature = FeatureEnabled;
        for (int i = 0; i < _controls.Length; i++)
            TickControl(_controls[i], feature);
    }

    private static void TickControl(DockedControl ctl, bool feature)
    {
        Transform? mount = feature ? ctl.FindMount() : null;
        bool mountReady = mount != null && mount.gameObject.activeInHierarchy;
        RectTransform? widget = mountReady ? ctl.FindWidget() : null;

        // Genuine teardown → release now: the host/target died (scene/hand teardown),
        // or the game swapped in a DIFFERENT widget instance (re-dock the new one).
        bool targetDead = ctl.Panel != null && !ctl.Panel.IsAlive;
        bool targetSwapped = ctl.Panel != null && widget != null
                             && !ReferenceEquals(ctl.Panel.Target, widget);

        // Transient null (the widget momentarily hid / the poll blipped): for a control
        // with hysteresis, hold the dock until the null has persisted past HoldSeconds
        // (test #25 item 1a — the anti-flicker hold). Without hysteresis a null releases
        // at once, as before. A live/matching widget clears the hold clock.
        bool transientNull = ctl.Panel != null && !targetDead && !targetSwapped && widget == null;
        if (transientNull && ctl.HoldOnTransientNull)
        {
            if (ctl.NullSince <= 0f)
                ctl.NullSince = Time.unscaledTime;
        }
        else
        {
            ctl.NullSince = 0f;
        }
        bool holdExpired = transientNull && ctl.HoldOnTransientNull
                           && Time.unscaledTime - ctl.NullSince > HoldSeconds;
        bool nullRelease = transientNull && (!ctl.HoldOnTransientNull || holdExpired);

        if (ctl.Panel != null && (targetDead || targetSwapped || nullRelease))
        {
            CanvasConversion.Release(ctl.Panel); // restores the widget's exact 2D home
            ctl.Panel = null;
            ctl.NullSince = 0f;
            if (ctl.DockLogged)
            {
                ctl.DockLogged = false;
                VRLog.Info("WorldUI", $"TRAY CONTROLS: '{ctl.Name}' native widget released — " +
                                      "restored to its 2D home (mod-drawn button takes over if still needed).");
            }
        }

        if (widget != null && ctl.Panel == null)
        {
            ctl.Panel = CanvasConversion.Convert(widget, $"TrayCtl_{ctl.Name}", pokeable: true);
            if (ctl.Panel != null)
            {
                // Native tray widgets convert at their own tight rect (e.g. the ~335x30
                // "Kurze Rast" bar) — unlike a full-screen window root they need no
                // content-fit. Disabling it keeps the host at the native rect (so the
                // poke/laser plane matches the real button exactly) and stops the
                // "nothing visible" shrink pass that only churned the log (test #25).
                ctl.Panel.FitEnabled = false;
                ctl.NullSince = 0f;
                if (!ctl.DockLogged)
                {
                    ctl.DockLogged = true;
                    VRLog.Info("WorldUI", $"TRAY CONTROLS: '{ctl.Name}' REAL game widget docked on the " +
                                          "control board (native sprite/label/enable-state, poke + laser) — " +
                                          "the mod-drawn button is suppressed while it holds.");
                }
            }
        }

        if (ctl.Panel != null && mount != null)
            Place(ctl, mount);
    }

    /// <summary>
    /// Pose-follow the tray mount (centered origin, the DecisionDock.Place pattern):
    /// fit the content-sized host rect into the control's tray-local meter budget,
    /// preserving aspect, times the mount's lossy scale (tray grab + rig scale).
    /// </summary>
    private static void Place(DockedControl ctl, Transform mount)
    {
        ConvertedPanel panel = ctl.Panel!;
        if (!panel.HostGo.activeSelf)
            panel.HostGo.SetActive(true);

        Rect rect = panel.HostRect.rect; // content-fitted by CanvasConversion.TickFit
        if (rect.width >= 1f && rect.height >= 1f)
        {
            float metersPerPx = Mathf.Min(ctl.TargetWidth / rect.width, ctl.TargetHeight / rect.height);
            Transform host = panel.HostTransform;
            host.SetPositionAndRotation(mount.position, mount.rotation); // centered, canvas faces the viewer
            host.localScale = Vector3.one * (metersPerPx * mount.lossyScale.x);
        }

        // Item 7: a docked host must never accept a click while its widget is not
        // rendering — the native ReadyButton can sit docked + interactable with its
        // canvasGroup alpha at ~0 (VR hides the 2D stack in the "all cards placed"
        // state), an invisible click-catcher. Stand the raycaster down whenever the
        // widget is not visibly rendering, so a docked collider never outlives its
        // renderer. Test #19 accident guard (post-drop CONFIRM suppression) AND the
        // mirrored UI lock also stand the Continue raycaster down.
        if (panel.HostRaycaster != null)
        {
            bool allow = HostRendering(panel)
                         && !CanvasConversion.IsLockedNow
                         && (!ctl.PostDropGuard
                             || PlayTray.Current == null
                             || PlayTray.Current.ConfirmGuardRemaining() <= 0f);
            if (panel.HostRaycaster.enabled != allow)
                panel.HostRaycaster.enabled = allow;
        }
    }

    /// <summary>
    /// Re-place every docked native control at the END of the frame. These hosts pose-follow tray
    /// mounts exactly like the converted panels do, and the board's carry writer
    /// (<c>PanelGrabHandle.Update</c>) has no execution-order relation to the WorldUI Update tick —
    /// so an Update-only copy renders a frame behind a board that is being moved (see
    /// <see cref="WorldSurface.LateTick"/> for the full derivation; the user asked for this
    /// rigidity "allgemein bei allen Elementen die an dem Controllboard dran sind"). Only the pose
    /// is re-derived; conversion, release and the hysteresis clocks stay on the Update tick.
    /// </summary>
    public void LateTick()
    {
        for (int i = 0; i < _controls.Length; i++)
        {
            DockedControl ctl = _controls[i];
            if (ctl.Panel == null || !ctl.Panel.IsAlive)
                continue;
            Transform? mount = ctl.FindMount();
            if (mount != null && mount.gameObject.activeInHierarchy)
                Place(ctl, mount);
        }
    }

    public void Shutdown()
    {
        for (int i = 0; i < _controls.Length; i++)
        {
            DockedControl ctl = _controls[i];
            if (ctl.Panel != null)
            {
                CanvasConversion.Release(ctl.Panel); // widget back in its 2D home
                ctl.Panel = null;
            }
            ctl.DockLogged = false;
        }
        if (ReferenceEquals(Instance, this))
            Instance = null; // docks drop → mod-drawn buttons own the controls again
    }

    /// <summary>One native control docked (or dockable) on the tray.</summary>
    private sealed class DockedControl
    {
        internal readonly string Name;
        internal readonly Func<RectTransform?> FindWidget;
        internal readonly Func<Transform?> FindMount;
        internal readonly float TargetWidth;
        internal readonly float TargetHeight;
        internal readonly bool PostDropGuard;

        /// <summary>
        /// Hold the dock through a MOMENTARY <c>FindWidget()==null</c> instead of
        /// releasing at once (test #25 item 1a hysteresis): the game toggles the
        /// short-rest widget's active/visible state across a phase blip or hand refresh,
        /// and a same-frame release/re-dock made the native widget and the mod-drawn
        /// button flicker against each other. Only a null that PERSISTS past
        /// <see cref="HoldSeconds"/> (or a genuinely dead/swapped target) releases.
        /// </summary>
        internal readonly bool HoldOnTransientNull;

        internal ConvertedPanel? Panel;
        internal bool DockLogged;

        /// <summary>Unscaled time the current transient-null hold began (0 = not holding).</summary>
        internal float NullSince;

        internal DockedControl(string name, Func<RectTransform?> findWidget, Func<Transform?> findMount,
            float targetWidth, float targetHeight, bool postDropGuard, bool holdOnTransientNull = false)
        {
            Name = name;
            FindWidget = findWidget;
            FindMount = findMount;
            TargetWidth = targetWidth;
            TargetHeight = targetHeight;
            PostDropGuard = postDropGuard;
            HoldOnTransientNull = holdOnTransientNull;
        }
    }
}
