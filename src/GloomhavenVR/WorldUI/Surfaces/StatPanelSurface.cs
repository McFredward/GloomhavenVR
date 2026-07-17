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

        // Preferred anchor: BESIDE the shown actor's miniature (test #16 — straight
        // above put the panel inside the very ray inspecting the miniature; sideways
        // of the head→miniature axis plus a small lift keeps it out of the ray path).
        Vector3? anchor = ResolveActorAnchor(watch);
        if (anchor.HasValue && head != null)
        {
            Vector3 fromHead = anchor.Value - head.transform.position;
            fromHead.y = 0f;
            if (fromHead.sqrMagnitude < 1e-4f)
                fromHead = Vector3.forward;
            fromHead.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fromHead); // unit: screen-right of the view axis
            Vector3 pos = anchor.Value + (side * 0.30f + Vector3.up * 0.10f) * scale;
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

    private static Vector3? ResolveActorAnchor(Watch watch)
    {
        Choreographer choreographer = Choreographer.s_Choreographer;
        if (choreographer == null)
            return null;

        ScenarioRuleLibrary.CActor? actor = watch.Attached switch
        {
            ActorStatPanel statPanel => statPanel.m_ActorShown,
            EnemyCurrentTurnStatPanel enemyPanel => enemyPanel._currentShownEnemy,
            _ => null,
        };
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
