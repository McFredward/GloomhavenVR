using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Shared implementation for scenario panels that live in a fixed
/// <see cref="PanelSlot"/> of the curved table layout.
/// </summary>
internal abstract class SlotPanelSurface : WorldSurface
{
    protected abstract PanelSlot Slot { get; }

    protected override void Place()
    {
        if (Panel == null || !PanelLayout.TryGetPose(Slot, out Vector3 pos, out Quaternion rot))
            return;
        CanvasConversion.PlaceHost(Panel, pos, rot, PanelLayout.WorldScale);
    }
}

/// <summary>
/// Test #15 dashboard seam: a converted panel DOCKED onto a <see cref="PlayTray"/>
/// mount anchor. The host POSE-FOLLOWS the mount every tick (position/rotation/
/// scale copied — it is never re-parented under the tray: the host carries live
/// GAME UI, and a tray/rig teardown must never cascade into destroying game-owned
/// canvases). Sized to the tray: the panel's width is fitted to the mount's target
/// width (height-capped), in tray-local meters × the mount's lossy scale — so it
/// survives tray grab/move/resize and diorama scaling for free. While the tray is
/// hidden the host hides with it; when no mount exists at all (Cards module off,
/// tray destroyed) the panel falls back to the old floating slot layout.
/// </summary>
internal abstract class TrayMountedPanelSurface : SlotPanelSurface
{
    /// <summary>Live mount anchor (null/destroyed → floating fallback).</summary>
    protected abstract Transform? Mount { get; }

    /// <summary>Target panel width, tray-local meters.</summary>
    protected abstract float MountWidth { get; }

    /// <summary>Max panel height, tray-local meters (caps the scale for tall content).</summary>
    protected abstract float MountMaxHeight { get; }

    /// <summary>Which way the panel extends from the mount origin (unit XY in mount space).</summary>
    protected abstract Vector2 GrowDirection { get; }

    protected override void Place()
    {
        if (Panel == null)
            return;

        Transform? mount = Mount; // Unity-null aware: destroyed mounts fall through
        if (mount == null)
        {
            if (!Panel.HostGo.activeSelf)
                Panel.HostGo.SetActive(true);
            base.Place(); // old floating layout (fallback per the mount-seam contract)
            return;
        }

        // Tray hidden (out-of-scenario transitions, hands down) → panel hides too.
        bool visible = mount.gameObject.activeInHierarchy;
        if (Panel.HostGo.activeSelf != visible)
            Panel.HostGo.SetActive(visible);
        if (!visible)
            return;

        Rect rect = Panel.HostRect.rect; // content-fitted by CanvasConversion.TickFit
        if (rect.width < 1f || rect.height < 1f)
            return;

        // Tray-local meters per uGUI pixel, capped by the height budget; the mount's
        // lossy scale carries BOTH the tray-grab scale and the diorama scale.
        float trayScale = mount.lossyScale.x;
        float metersPerPx = Mathf.Min(MountWidth / rect.width, MountMaxHeight / rect.height);

        Vector2 grow = GrowDirection;
        Vector3 offset = new Vector3(
            grow.x * rect.width * metersPerPx * 0.5f,
            grow.y * rect.height * metersPerPx * 0.5f,
            0f) * trayScale;

        Transform host = Panel.HostTransform;
        host.SetPositionAndRotation(mount.position + mount.rotation * offset, mount.rotation);
        host.localScale = Vector3.one * (metersPerPx * trayScale);
    }
}

/// <summary>
/// The game's REAL initiative track, docked to the tray dashboard's TOP edge
/// (test #15; the free-floating world panel is gone — 'die Elemente fliegen
/// aktuell rum'). Target verified via ilspycmd (GH.Runtime.dll):
/// <c>public class InitiativeTrack : MonoBehaviour</c> with <c>public static
/// InitiativeTrack Instance { get; private set; }</c> — the component sits at the
/// track's uGUI root, so its own RectTransform is the conversion target. Avatars
/// stay fully interactive (initiative swap pokes go through the host
/// GraphicRaycaster). The vanilla not-locked-in display comes with it for free:
/// <c>InitiativeTrackActorAvatar</c> (decompiled GH.Runtime, ilspycmd) owns it —
/// <c>private const string Unknown = "?"</c>, <c>ViewDigitInitiative()</c> shows
/// <c>m_DigitViewer.ViewSpecialSymbol("?")</c> for initiative 0, and
/// <c>InitiativeTrackPlayerAvatar.CalculateInitiative</c> returns "?" for online
/// players who have not locked in (<c>!actor.IsUnderMyControl</c> during
/// <c>SelectAbilityCardsOrLongRest</c>).
/// </summary>
internal sealed class InitiativeTrackSurface : TrayMountedPanelSurface
{
    public override string Name => "InitiativeTrack";
    protected override bool ConfigEnabled => WorldUIConfig.InitiativeTrack.Value;
    protected override PanelSlot Slot => PanelSlot.InitiativeTrack;
    protected override Transform? Mount => PlayTray.Current?.InitiativeMount;
    protected override float MountWidth => PlayTray.InitiativeMountWidth;
    protected override float MountMaxHeight => PlayTray.InitiativeMountMaxHeight;
    protected override Vector2 GrowDirection => Vector2.up; // bottom edge on the mount

    protected override RectTransform? FindTarget() =>
        InitiativeTrack.Instance != null ? InitiativeTrack.Instance.transform as RectTransform : null;
}

/// <summary>
/// Element infusion board as a world panel near the initiative track.
/// Verified: <c>public class InfusionBoardUI : MonoBehaviour</c> with
/// <c>public static InfusionBoardUI Instance { get; private set; }</c>.
/// </summary>
internal sealed class ElementBoardSurface : SlotPanelSurface
{
    public override string Name => "ElementBoard";
    protected override bool ConfigEnabled => WorldUIConfig.ElementBoard.Value;
    protected override PanelSlot Slot => PanelSlot.ElementBoard;

    protected override RectTransform? FindTarget() =>
        InfusionBoardUI.Instance != null ? InfusionBoardUI.Instance.transform as RectTransform : null;
}

/// <summary>
/// Combat log as a smaller world panel, farther out on the arc.
/// Verified: <c>[RequireComponent(typeof(UIWindow))] public class CombatLogHandler :
/// Singleton&lt;CombatLogHandler&gt;, IPointerEnterHandler, ...</c>. The ScrollRect keeps
/// working — poke drags synthesize real pointer events on the host raycaster.
/// </summary>
internal sealed class CombatLogSurface : SlotPanelSurface
{
    public override string Name => "CombatLog";
    protected override bool ConfigEnabled => WorldUIConfig.CombatLog.Value;
    protected override PanelSlot Slot => PanelSlot.CombatLog;

    protected override RectTransform? FindTarget() =>
        Singleton<CombatLogHandler>.IsInitialized
            ? Singleton<CombatLogHandler>.Instance.transform as RectTransform
            : null;
}

/// <summary>
/// Scenario objectives ('das Ziel'), docked to the tray dashboard's LEFT edge
/// (test #15; floating slot layout only as fallback).
/// Verified: <c>UIManager.MissionObjectiveContainer</c> property
/// (<c>public MissionObjectiveContainer MissionObjectiveContainer =&gt;
/// missionObjectiveContainer;</c>) — populated by <c>UIManager.InitScenario</c>.
/// </summary>
internal sealed class ObjectivesSurface : TrayMountedPanelSurface
{
    public override string Name => "Objectives";
    protected override bool ConfigEnabled => WorldUIConfig.Objectives.Value;
    protected override PanelSlot Slot => PanelSlot.Objectives;
    protected override Transform? Mount => PlayTray.Current?.ObjectivesMount;
    protected override float MountWidth => PlayTray.ObjectivesMountWidth;
    protected override float MountMaxHeight => PlayTray.ObjectivesMountMaxHeight;
    protected override Vector2 GrowDirection => Vector2.left; // right edge on the mount

    protected override RectTransform? FindTarget()
    {
        UIManager manager = UIManager.Instance;
        return manager != null && manager.MissionObjectiveContainer != null
            ? manager.MissionObjectiveContainer.transform as RectTransform
            : null;
    }
}
