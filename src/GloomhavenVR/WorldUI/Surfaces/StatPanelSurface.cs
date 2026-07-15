using GloomhavenVR.Core;
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
/// The panel is anchored next to the shown actor's miniature when its client
/// GameObject resolves, else it falls back to the StatPanel layout slot.
/// </summary>
internal sealed class StatPanelSurface
{
    private sealed class Watch
    {
        public Component? Attached;
        public UIWindow? Window;
        public ConvertedPanel? Panel;
        public bool PendingShow;
        public UnityEngine.Events.UnityAction OnShown = null!;
        public UnityEngine.Events.UnityAction OnHidden = null!;
    }

    private readonly Watch _actorPanel = new();
    private readonly Watch _enemyTurnPanel = new();

    public string Name => "StatPanel";

    public StatPanelSurface()
    {
        _actorPanel.OnShown = () => _actorPanel.PendingShow = true;
        _actorPanel.OnHidden = () => Release(_actorPanel);
        _enemyTurnPanel.OnShown = () => _enemyTurnPanel.PendingShow = true;
        _enemyTurnPanel.OnHidden = () => Release(_enemyTurnPanel);
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
            if (WorldUIConfig.StatPanels.Value && WorldUIConfig.ConversionActive
                && Choreographer.s_Choreographer != null && watch.Panel == null && watch.Attached != null)
            {
                watch.Panel = CanvasConversion.Convert(watch.Attached.transform as RectTransform, name);
                if (watch.Panel != null)
                    PlaceWatch(watch);
            }
        }

        // Keep facing the player (billboard yaw only; cheap, allocation-free).
        if (watch.Panel != null)
            PlaceWatch(watch);
    }

    private void PlaceWatch(Watch watch)
    {
        if (watch.Panel == null)
            return;

        float scale = PanelLayout.WorldScale;
        Camera? head = CanvasConversion.WorldCamera;

        // Preferred anchor: the shown actor's miniature.
        Vector3? anchor = ResolveActorAnchor(watch);
        if (anchor.HasValue && head != null)
        {
            Vector3 pos = anchor.Value + Vector3.up * (0.22f * scale);
            Vector3 fromHead = pos - head.transform.position;
            fromHead.y = 0f;
            if (fromHead.sqrMagnitude < 1e-4f)
                fromHead = Vector3.forward;
            Quaternion rot = Quaternion.LookRotation(fromHead.normalized, Vector3.up);
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

    private void Release(Watch watch)
    {
        watch.PendingShow = false;
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
        DetachWatch(_actorPanel);
        DetachWatch(_enemyTurnPanel);
    }
}
