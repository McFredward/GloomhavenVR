using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Actor/monster stat panels as world panels near the inspected miniature
/// (ARCHITECTURE §7 — Phase 3a triggers the open by poking a miniature; this class
/// owns only the presentation).
///
/// Verified via ilspycmd (GH.Runtime.dll):
/// <code>
///   [RequireComponent(typeof(UIWindow))] public class ActorStatPanel : Singleton&lt;ActorStatPanel&gt;
///     public void Show(CActor actor)            // sets private CActor m_ActorShown (publicized)
///   [RequireComponent(typeof(UIWindow))] public class EnemyCurrentTurnStatPanel : Singleton&lt;...&gt;
///     public void Show(CEnemyActor enemyActor)  // sets private CEnemyActor _currentShownEnemy
///   Choreographer: public GameObject FindClientActorGameObject(CActor actor,
///       bool shouldReturnDummyActorsProp = false)
/// </code>
/// Both windows announce visibility through their UIWindow onShown/onHidden events.
/// The panel is anchored BESIDE the shown actor's miniature when its client
/// GameObject resolves, else it falls back to the StatPanel layout slot.
///
/// TEST #16 — the stat panel must never intercept the pointer:
/// - Converted NON-pokeable: both panels are purely informational (verified in the
///   decompiled classes: no Button/Selectable members, only viewers), so the host is
///   never registered in UguiPokeSurfaces — the far ray (RayUguiDriver) and fingertip
///   poke ignore it, and our UIManager.IsPointerOverUI patch (which reports
///   Ray.HasFreshUiHit / Poke.HoveredUi — registered surfaces only) can never go
///   true because of it. The host GraphicRaycaster is kept disabled too, so the
///   vanilla EventSystem answer ignores it as well.
/// - Anchored to the SIDE of the miniature, not straight above it: at +0.22 up the
///   panel sat exactly in the hand ray that was inspecting the miniature → the ray
///   hit the panel → IsPointerOverUI true → the game hid the panel → our release put
///   the ray back on the board → the game re-showed it — a 45 Hz show/hide loop that
///   also broke placement hover-arming.
/// - Release HYSTERESIS: a hide only releases the conversion after
///   <see cref="ReleaseDelaySeconds"/>; a re-show within the window simply cancels
///   the pending release (no re-conversion). If a window still churns
///   (&gt;<see cref="ChurnWarnCount"/> conversions in <see cref="ChurnWindowSeconds"/>)
///   one warning names it.
/// </summary>
internal sealed class StatPanelSurface
{
    /// <summary>Hide→release hysteresis (unscaled seconds) — absorbs show/hide flicker.</summary>
    private const float ReleaseDelaySeconds = 0.3f;

    /// <summary>Churn telemetry: rolling window for the conversion counter (unscaled seconds).</summary>
    private const float ChurnWindowSeconds = 2f;

    /// <summary>Conversions inside one window above which the single churn warning fires.</summary>
    private const int ChurnWarnCount = 5;

    private sealed class Watch
    {
        public Component? Attached;
        public UIWindow? Window;
        public ConvertedPanel? Panel;
        public bool PendingShow;

        /// <summary>Unscaled time at which a scheduled release fires; 0 = none pending.</summary>
        public float ReleaseAt;

        // Churn telemetry (test #16): conversions inside the rolling window.
        public float CycleWindowStart;
        public int CycleCount;
        public bool ChurnWarned;

        public UnityEngine.Events.UnityAction OnShown = null!;
        public UnityEngine.Events.UnityAction OnHidden = null!;
    }

    private readonly Watch _actorPanel = new();
    private readonly Watch _enemyTurnPanel = new();

    // ---- held-figure anchor override (P8, figure-grab) --------------------------------
    // While a figure is held in the hand, the SAME ActorStatPanel is docked next to the
    // held mini instead of its board cell (billboarded to the head, tighter offset so it
    // clears the hand). Static because the setter is the Board-side FigureGrabbable, which
    // does not own a StatPanelSurface instance.
    private static Transform? _heldAnchor;
    private static ScenarioRuleLibrary.CActor? _heldActor;

    /// <summary>Held-figure offset (side / up, real meters × world scale) — snug so it clears the hand.</summary>
    private const float HeldSideOffset = 0.15f;
    private const float HeldUpOffset = 0.05f;

    public string Name => "StatPanel";

    public StatPanelSurface()
    {
        _actorPanel.OnShown = () => _actorPanel.PendingShow = true;
        _actorPanel.OnHidden = () => ScheduleRelease(_actorPanel);
        _enemyTurnPanel.OnShown = () => _enemyTurnPanel.PendingShow = true;
        _enemyTurnPanel.OnHidden = () => ScheduleRelease(_enemyTurnPanel);

        // P5 (MISSION A.8): Board announces miniature pokes on the bus; opening the
        // game's own ActorStatPanel window here triggers the conversion above via its
        // UIWindow onShown — the presentation pipeline is unchanged.
        VREvents.MiniaturePoked += OnMiniaturePoked;
    }

    /// <summary>
    /// P8: dock the ActorStatPanel next to a figure held in the hand. Opens the same
    /// window the laser mouse-over uses (<c>ActorStatPanel.Show</c>) and records the held
    /// anchor so <see cref="PlaceWatch"/> follows the mini into the hand.
    /// </summary>
    internal static void ShowHeldFigure(Transform anchor, ScenarioRuleLibrary.CActor? actor)
    {
        _heldAnchor = anchor;
        _heldActor = actor;
        if (actor == null || !WorldUIConfig.StatPanels.Value || !WorldUIConfig.ConversionActive)
            return;
        if (CanvasConversion.IsLockedNow || !Singleton<ActorStatPanel>.IsInitialized)
            return;
        ActorStatPanel.Instance.Show(actor);
    }

    /// <summary>Clear the held-figure override on release (only if <paramref name="actor"/> owns it).</summary>
    internal static void ClearHeldFigure(ScenarioRuleLibrary.CActor? actor)
    {
        if (_heldActor != null && actor != null && !ReferenceEquals(actor, _heldActor))
            return; // a different held figure still owns the panel
        _heldAnchor = null;
        _heldActor = null;
    }

    /// <summary>
    /// Re-open the held figure's stat card if the game's board hover re-targeted the shared
    /// panel to another actor (risk #5). Cheap: only re-Shows when the shown actor drifted.
    /// </summary>
    internal static void ReassertHeld()
    {
        if (_heldActor == null || !Singleton<ActorStatPanel>.IsInitialized || CanvasConversion.IsLockedNow)
            return;
        if (!ReferenceEquals(ActorStatPanel.Instance.m_ActorShown, _heldActor))
            ActorStatPanel.Instance.Show(_heldActor);
    }

    private static void OnMiniaturePoked(MiniaturePokedEvent e)
    {
        if (!WorldUIConfig.StatPanels.Value || !WorldUIConfig.ConversionActive)
            return;
        if (CanvasConversion.IsLockedNow)
            return; // modality: no popups while the game locked its UI
        if (!Singleton<ActorStatPanel>.IsInitialized || e.Actor == null)
            return;
        // Verified via ilspycmd (GH.Runtime.dll): public void Show(CActor actor).
        ActorStatPanel.Instance.Show(e.Actor);
    }

    public void Tick()
    {
        TickWatch(_actorPanel,
            Singleton<ActorStatPanel>.IsInitialized ? Singleton<ActorStatPanel>.Instance : null,
            "ActorStatPanel");
        TickWatch(_enemyTurnPanel,
            Singleton<EnemyCurrentTurnStatPanel>.IsInitialized ? Singleton<EnemyCurrentTurnStatPanel>.Instance : null,
            "EnemyCurrentTurnStatPanel");
    }

    private void TickWatch(Watch watch, Component? live, string name)
    {
        if (watch.Panel != null && !watch.Panel.IsAlive)
            watch.Panel = null;

        if (!ReferenceEquals(live, watch.Attached))
        {
            DetachWatch(watch);
            watch.Attached = live;
            watch.Window = live != null ? live.GetComponent<UIWindow>() : null;
            if (watch.Window != null)
            {
                watch.Window.onShown.AddListener(watch.OnShown);
                watch.Window.onHidden.AddListener(watch.OnHidden);
                if (watch.Window.IsOpen)
                    watch.PendingShow = true;
            }
        }

        if (watch.PendingShow)
        {
            watch.PendingShow = false;
            if (watch.Panel != null)
            {
                // Re-shown inside the hysteresis window — keep the live conversion.
                watch.ReleaseAt = 0f;
            }
            else if (WorldUIConfig.StatPanels.Value && WorldUIConfig.ConversionActive
                && Choreographer.s_Choreographer != null && watch.Attached != null)
            {
                // Informational panel (no buttons, verified) — NOT pokeable: never in
                // UguiPokeSurfaces, so neither ray nor poke nor IsPointerOverUI see it.
                watch.Panel = CanvasConversion.Convert(watch.Attached.transform as RectTransform, name,
                    pokeable: false);
                if (watch.Panel != null)
                {
                    CountConversion(watch, name);
                    PlaceWatch(watch);
                }
            }
        }

        // Deferred release (hysteresis): the window stayed hidden past the delay.
        if (watch.Panel != null && watch.ReleaseAt > 0f && Time.unscaledTime >= watch.ReleaseAt)
            Release(watch);

        // Keep facing the player (billboard yaw only; cheap, allocation-free).
        if (watch.Panel != null)
        {
            // The lock mirror in CanvasConversion.Tick may re-enable host raycasters
            // wholesale — keep this one dark so the vanilla EventSystem never hits it.
            if (watch.Panel.HostRaycaster != null && watch.Panel.HostRaycaster.enabled)
                watch.Panel.HostRaycaster.enabled = false;
            PlaceWatch(watch);
        }
    }

    /// <summary>
    /// Hide → deferred release (test #16). The game's hover logic hides/re-shows the
    /// panel liberally; releasing instantly re-parented the whole uGUI subtree at
    /// frame rate. A re-show within the window cancels the pending release.
    /// </summary>
    private static void ScheduleRelease(Watch watch)
    {
        watch.PendingShow = false;
        if (watch.Panel != null)
            watch.ReleaseAt = Time.unscaledTime + ReleaseDelaySeconds;
    }

    /// <summary>One warning if a panel still churns through conversions (test #16).</summary>
    private static void CountConversion(Watch watch, string name)
    {
        float now = Time.unscaledTime;
        if (now - watch.CycleWindowStart > ChurnWindowSeconds)
        {
            watch.CycleWindowStart = now;
            watch.CycleCount = 0;
        }
        watch.CycleCount++;
        if (watch.CycleCount > ChurnWarnCount && !watch.ChurnWarned)
        {
            watch.ChurnWarned = true;
            VRLog.Warn("WorldUI", $"{name} convert/release churn: >{ChurnWarnCount} conversions in " +
                                  $"{ChurnWindowSeconds:F0}s despite the {ReleaseDelaySeconds:F1}s release " +
                                  "hysteresis — something still occludes/toggles the window per frame.");
        }
    }

    private void PlaceWatch(Watch watch)
    {
        if (watch.Panel == null)
            return;

        float scale = PanelLayout.WorldScale;
        Camera? head = CanvasConversion.WorldCamera;

        // Held-figure override (P8): if THIS watch shows the figure currently held in the
        // hand, anchor beside the held mini (tighter offset so it clears the hand) instead
        // of its board cell — always billboarded to face the player, same as the board case.
        bool held = _heldAnchor != null && ReferenceEquals(WatchActor(watch), _heldActor);
        Vector3? anchor = held ? _heldAnchor!.position : ResolveActorAnchor(watch);
        if (anchor.HasValue && head != null)
        {
            float sideOffset = held ? HeldSideOffset : 0.30f;
            float upOffset = held ? HeldUpOffset : 0.10f;

            Vector3 fromHead = anchor.Value - head.transform.position;
            fromHead.y = 0f;
            if (fromHead.sqrMagnitude < 1e-4f)
                fromHead = Vector3.forward;
            fromHead.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fromHead); // unit: screen-right of the view axis
            Vector3 pos = anchor.Value + (side * sideOffset + Vector3.up * upOffset) * scale;
            Vector3 facing = pos - head.transform.position;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f)
                facing = fromHead;
            Quaternion rot = Quaternion.LookRotation(facing.normalized, Vector3.up);
            CanvasConversion.PlaceHost(watch.Panel, pos, rot, scale * 0.6f);
            return;
        }

        // Fallback: fixed layout slot.
        if (PanelLayout.TryGetPose(PanelSlot.StatPanel, out Vector3 slotPos, out Quaternion slotRot))
            CanvasConversion.PlaceHost(watch.Panel, slotPos, slotRot, scale * 0.6f);
    }

    /// <summary>The CActor currently shown by a watch's window (actor panel or enemy-turn panel).</summary>
    private static ScenarioRuleLibrary.CActor? WatchActor(Watch watch) => watch.Attached switch
    {
        ActorStatPanel statPanel => statPanel.m_ActorShown,
        EnemyCurrentTurnStatPanel enemyPanel => enemyPanel._currentShownEnemy,
        _ => null,
    };

    private static Vector3? ResolveActorAnchor(Watch watch)
    {
        Choreographer choreographer = Choreographer.s_Choreographer;
        if (choreographer == null)
            return null;

        ScenarioRuleLibrary.CActor? actor = WatchActor(watch);
        if (actor == null)
            return null;

        GameObject clientGo = choreographer.FindClientActorGameObject(actor);
        return clientGo != null ? clientGo.transform.position : null;
    }

    private static void Release(Watch watch)
    {
        watch.PendingShow = false;
        watch.ReleaseAt = 0f;
        if (watch.Panel != null)
        {
            CanvasConversion.Release(watch.Panel);
            watch.Panel = null;
        }
    }

    private void DetachWatch(Watch watch)
    {
        if (watch.Window != null)
        {
            watch.Window.onShown.RemoveListener(watch.OnShown);
            watch.Window.onHidden.RemoveListener(watch.OnHidden);
        }
        watch.Window = null;
        watch.Attached = null;
        Release(watch);
    }

    public void Shutdown()
    {
        VREvents.MiniaturePoked -= OnMiniaturePoked;
        DetachWatch(_actorPanel);
        DetachWatch(_enemyTurnPanel);
    }
}
