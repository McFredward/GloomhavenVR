using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
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

    private readonly DockedControl _continue;
    private readonly DockedControl _undo;
    private readonly DockedControl _shortRest;
    private readonly DockedControl[] _controls;

    public TrayControlDockSurface()
    {
        Instance = this;
        // Target sizes in tray-local meters (× mount lossy scale), matching the
        // footprints the mod buttons occupy: CONFIRM 0.115×0.06, UNDO 0.09×0.042.
        // Test #24 item 3: the native "Kurze Rast" widget is a wide, short bar
        // (~335×30 px) — the fit binds on WIDTH, so the target width sets its size.
        // Fill the widened 0.14 rest plate's button footprint (0.115×0.04) with a
        // little vertical headroom so it reads comfortably, not tiny.
        _continue = new DockedControl("Continue", CardsGameApi.ReadyWidget,
            static () => PlayTray.Current?.ContinueMount, 0.12f, 0.062f, postDropGuard: true);
        _undo = new DockedControl("Undo", CardsGameApi.UndoWidget,
            static () => PlayTray.Current?.UndoDockMount, 0.10f, 0.052f, postDropGuard: false);
        // Test #25 item 1a: the native "Kurze Rast" bar is the ONLY control the player
        // wants (no mod-drawn fallback flicker), and it should read comfortably BIGGER —
        // width bumped 0.13→0.16 (the bar is width-bound, ~335x30 px, so a wider target
        // scales the whole widget up). It also holds its dock through transient hides
        // (holdOnTransientNull) so it never rapid-cycles against the mod button.
        _shortRest = new DockedControl("ShortRest", CardsGameApi.ShortRestWidget,
            static () => PlayTray.Current?.ShortRestAnchor, 0.16f, 0.055f, postDropGuard: false,
            holdOnTransientNull: true);
        _controls = new[] { _continue, _undo, _shortRest };
    }

    /// <summary>True while the REAL Continue/Confirm (ReadyButton) is docked on the tray.</summary>
    internal static bool ContinueDocked => Instance != null && Instance._continue.Panel != null;

    /// <summary>True while the REAL Undo (UndoButton) is docked on the tray.</summary>
    internal static bool UndoDocked => Instance != null && Instance._undo.Panel != null;

    /// <summary>True while the REAL short-rest (ShortRest) widget is docked on the tray.</summary>
    internal static bool ShortRestDocked => Instance != null && Instance._shortRest.Panel != null;

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

        // Test #19 accident guard on the docked Continue button: while the post-drop
        // suppression window runs (a card was just dropped/plucked near the slots),
        // AND while the game UI is locked, the host must not accept a click. Other
        // controls follow the plain lock mirror (CanvasConversion re-applies it).
        if (ctl.PostDropGuard && panel.HostRaycaster != null)
        {
            bool allow = !CanvasConversion.IsLockedNow
                         && (PlayTray.Current == null || PlayTray.Current.ConfirmGuardRemaining() <= 0f);
            if (panel.HostRaycaster.enabled != allow)
                panel.HostRaycaster.enabled = allow;
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
