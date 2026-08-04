using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// DAMAGE TOOLTIP (test #23, item 5). While the take-damage widget row is docked on the
/// control board (<see cref="DecisionDockSurface"/>), <c>TakeDamagePanel.ShowDamageTooltip</c>
/// (TakeDamagePanel.cs:319) pushes the hover/mandatory-use hint into a <c>HelpBox</c>
/// window — usually <c>InitiativeTrack.Instance.helpBox</c> (the plain "deal damage" tip,
/// :354) or the global <c>Singleton&lt;HelpBox&gt;</c> (the lethal mandatory-use hint,
/// :335). Both are self-contained <c>[RequireComponent(typeof(UIWindow))]</c> windows with
/// their own <c>Canvas</c> (HelpBox.cs:7/49), rendered by the perspective UI camera. In VR
/// that reads two ways the user flagged:
///   - 3D-TILTED: the HelpBox content carries baked local rotations / z offsets, the same
///     styling that tilts the combat log — it needs the <see cref="Flatten2D"/> pass.
///   - TOO HIGH: <c>InitiativeTrack.helpBox</c> sits up by the initiative track, nowhere
///     near the docked buttons the player is reading.
/// This surface converts whichever HelpBox is currently showing the damage tip (flattened)
/// and parks it just ABOVE the docked widget row, facing the player — adjacent to the
/// buttons, not floating high. It is a strict descendant of the take-damage dock: it only
/// runs while <see cref="DecisionDockSurface.DockingTakeDamage"/> holds and the tip window
/// is open, and it restores the HelpBox to its exact 2D home (CanvasConversion restore
/// records) the moment either drops — nothing is destroyed.
///
/// NOT A HOVER TOOLTIP — NEVER SEAT THIS IN THE BOARD TOOLTIP AREA (user ruling 2026-08-04,
/// the ModBuild-46 regression): despite the game's "GUI_TOOLTIP_*" loc keys, this HelpBox is
/// the PERSISTENT instruction line of the take-damage decision flow — TakeDamagePanel shows
/// it when the prompt opens / a toggle clears (ShowDamageTooltip, TakeDamagePanel.cs:319,
/// called from ClearSelectedToggle:314 and ToggleVisibility:1068) and hides it only when the
/// prompt closes (ResetAndHide:1044). Nothing about it is pointer-driven. It IS "der Text der
/// Entscheidungsknoepfe": together with the row it forms the decision dock, and the user's
/// board-local <c>[Cards] DecisionGap_*</c> stepper tunes the distance between exactly this
/// text and the buttons — a distance that only means anything while the text sits over the
/// row. ModBuild 46 rerouted this surface through
/// <c>WorldTooltips.TryGetBoardAreaPose</c> ("every board-owned tooltip in one area"), which
/// tore the prompt text away from its buttons into the top-left tooltip corner and voided the
/// tuned gap — the user's rule is narrower: the tooltip area is ONLY for true MOUSEOVER
/// tooltips (things shown because the pointer hovers something, i.e. the shared
/// <c>UITooltip</c> canvas <c>WorldTooltips</c> presents). Persistent flow text stays with
/// the surface that owns it.
///
/// The global HelpBox doubles as the game's general hint strip; converting it here only
/// ever happens DURING the take-damage dock (where the strip is showing the damage tip),
/// and reverses on undock, so its normal use elsewhere is untouched.
/// </summary>
internal sealed class DamageTooltipSurface : WorldSurface
{
    /// <summary>Density-scale guards, mirroring <see cref="DecisionDockSurface"/>.</summary>
    private const float MaxDensityScale = 1f;
    private const float MinDensityScale = 0.5f;

    /// <summary>Tooltip text must stay readable under pressure — same density as the docked row.</summary>
    private const float DensityScale = 0.8f;

    /// <summary>Park the tip this far ABOVE the widget-row mount (mount-local metres), so it sits just over the buttons.</summary>
    private const float AboveRowMetres = 0.11f;

    /// <summary>The HelpBox window currently converted (open + showing the damage tip).</summary>
    private HelpBox? _active;

    public override string Name => "DamageTooltip";
    protected override bool ConfigEnabled => WorldUIConfig.DecisionDock.Value;
    protected override bool Flatten2D => true;

    /// <summary>
    /// The HelpBox showing the damage tip right now: prefer the initiative-track box
    /// (the common "deal damage" tip that appears too high), fall back to the global
    /// box (the lethal mandatory-use hint). Null when neither is open.
    /// </summary>
    private static HelpBox? OpenTip()
    {
        InitiativeTrack? track = InitiativeTrack.Instance;
        HelpBox? trackBox = track != null ? track.helpBox : null;
        if (IsOpen(trackBox))
            return trackBox;
        HelpBox? global = Singleton<HelpBox>.IsInitialized ? Singleton<HelpBox>.Instance : null;
        if (IsOpen(global))
            return global;
        return null;
    }

    private static bool IsOpen(HelpBox? box) =>
        box != null && box.myWindow != null && box.myWindow.IsOpen;

    protected override bool WantConverted
    {
        get
        {
            if (!base.WantConverted || FlatScreen.ManualScreenActive || !DecisionDockSurface.DockingTakeDamage)
                return false;
            return OpenTip() != null;
        }
    }

    protected override RectTransform? FindTarget()
    {
        _active = OpenTip();
        return _active != null ? _active.transform as RectTransform : null;
    }

    public override void Tick()
    {
        bool hadPanel = Panel != null;
        base.Tick(); // convert / release / Place (level-triggered on WantConverted)

        if (Panel != null && !hadPanel)
            VRLog.Info("WorldUI", "DAMAGE TOOLTIP: HelpBox docked flat above the take-damage row " +
                                  "(flattened, brought down beside the buttons) — restored to 2D when it closes.");
        else if (Panel == null && hadPanel)
        {
            _active = null;
            VRLog.Info("WorldUI", "DAMAGE TOOLTIP: HelpBox released — restored to its 2D home.");
        }
    }

    /// <summary>
    /// Park just above the docked widget-row mount (the buttons the tip describes),
    /// content-fitted into the same width budget and facing the player. While no mount
    /// exists the surface simply holds off (the dock itself has already floated to the
    /// HMD in that case; a mispositioned tip is never a lock).
    ///
    /// ROOT CAUSE of the ModBuild-46 regression this reverts ("Der Text der
    /// Entscheidungsknoepfe ... ist ploetzlich Teil der Tooltip-Section"): commit 6cfab0f
    /// seated this pose through <c>WorldTooltips.TryGetBoardAreaPose</c> on the reading
    /// "every board-owned tooltip belongs in the unified area". Wrong classification — see
    /// the class doc: this HelpBox is the decision dock's persistent PROMPT TEXT, not a
    /// mouseover tooltip, and moving it to the board's top-left corner (a) split the dock's
    /// text from its buttons and (b) made the user-tuned <c>[Cards] DecisionGap_*</c>
    /// text-to-buttons distance meaningless (DecisionDockSurface.Place anchors the widget
    /// block a gap below the prompt reference at the board's lower edge — the spot this text
    /// occupies). Restored verbatim to the pre-46 seat: AboveRowMetres over the
    /// DecisionMount, at the mount's own rotation and tray scale. The tooltip area stays
    /// reserved for the genuine hover tooltips WorldTooltips presents.
    /// </summary>
    protected override void Place()
    {
        if (Panel == null)
            return;

        Transform? mount = PlayTray.Current?.DecisionMount; // Unity-null aware
        if (mount == null || !mount.gameObject.activeInHierarchy)
        {
            Panel.OrderCluster = null; // no board — not part of any draw cluster
            return;
        }
        // Board-owned tip parked on the board plane: join the board's draw-order cluster so the
        // board's transparent furniture stays structurally below it — see
        // ConvertedPanel.OrderCluster.
        Panel.OrderCluster = PlayTray.Current;

        if (!Panel.HostGo.activeSelf)
            Panel.HostGo.SetActive(true);

        Rect rect = Panel.HostRect.rect; // content-fitted by CanvasConversion.TickFit
        if (rect.width < 1f || rect.height < 1f)
            return;

        float trayScale = mount.lossyScale.x;
        float density = PlayTray.TrayPixelsPerMeter * DensityScale;
        float fitScale = Mathf.Min(
            PlayTray.DecisionMountWidth * density / rect.width,
            PlayTray.DecisionMountMaxHeight * density / rect.height);
        float metersPerPx = Mathf.Clamp(fitScale, MinDensityScale, MaxDensityScale) / density;

        Transform host = Panel.HostTransform;
        Vector3 pos = mount.position + mount.up * (AboveRowMetres * trayScale);
        host.SetPositionAndRotation(pos, mount.rotation);
        host.localScale = Vector3.one * (metersPerPx * trayScale);
    }

    /// <summary>
    /// Re-place at the end of the frame: this tip pose-follows a control-board mount, so like every
    /// other board-docked element it must be written AFTER the board's own Update-phase carry writer
    /// or it renders one frame behind a moving board — see <see cref="WorldSurface.LateTick"/>.
    /// </summary>
    public override void LateTick()
    {
        if (Panel != null)
            Place();
    }

    public override void Shutdown()
    {
        base.Shutdown(); // releases the conversion → HelpBox back in its 2D home
        _active = null;
    }
}
