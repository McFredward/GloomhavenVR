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
/// Initiative track as a world panel above the far table edge (ARCHITECTURE §7).
/// Target verified via ilspycmd (GH.Runtime.dll): <c>public class InitiativeTrack :
/// MonoBehaviour</c> with <c>public static InitiativeTrack Instance { get; private
/// set; }</c> and its own nested <c>[SerializeField] private Canvas canvas;</c>
/// (sortingOrder 40) — the component sits at the track's uGUI root, so its own
/// RectTransform is the conversion target. Avatars stay fully interactive
/// (initiative swap pokes go through the host GraphicRaycaster).
/// </summary>
internal sealed class InitiativeTrackSurface : SlotPanelSurface
{
    public override string Name => "InitiativeTrack";
    protected override bool ConfigEnabled => WorldUIConfig.InitiativeTrack.Value;
    protected override PanelSlot Slot => PanelSlot.InitiativeTrack;

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
/// Scenario objectives as a world panel on the far arc.
/// Verified: <c>UIManager.MissionObjectiveContainer</c> property
/// (<c>public MissionObjectiveContainer MissionObjectiveContainer =&gt;
/// missionObjectiveContainer;</c>) — populated by <c>UIManager.InitScenario</c>.
/// </summary>
internal sealed class ObjectivesSurface : SlotPanelSurface
{
    public override string Name => "Objectives";
    protected override bool ConfigEnabled => WorldUIConfig.Objectives.Value;
    protected override PanelSlot Slot => PanelSlot.Objectives;

    protected override RectTransform? FindTarget()
    {
        UIManager manager = UIManager.Instance;
        return manager != null && manager.MissionObjectiveContainer != null
            ? manager.MissionObjectiveContainer.transform as RectTransform
            : null;
    }
}
