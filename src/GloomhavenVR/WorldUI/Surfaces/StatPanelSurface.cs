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

    // ---- held-figure anchor override (P8, figure-grab) — PER HAND ---------------------
    // TASK #4 — a figure can be held in EACH hand, and BOTH get an info panel docked at their
    // own hand. We keep PER-HAND registrations (set by the Board-side FigureGrabbable, which does
    // not own a StatPanelSurface instance — hence static) and drive TWO panels from them:
    //   • the game's SINGLE ActorStatPanel Singleton shows the PRIMARY held figure (the first hand
    //     to grab — so a single hold behaves EXACTLY as before), docked at that hand;
    //   • a lazily-created CLONE of that panel (a second ActorStatPanel instance, Instantiated and
    //     bound to the second CActor, with the game Singleton left pointing at the real one) shows
    //     the SECONDARY held figure, docked at the OTHER hand.
    // If only one hand holds, no clone is created and behaviour is identical to the old single panel.
    private readonly struct HeldReg
    {
        public HeldReg(Transform anchor, ScenarioRuleLibrary.CActor actor, GloomhavenVR.Hands.HandSide side, long seq)
        { Anchor = anchor; Actor = actor; Side = side; Seq = seq; }
        public readonly Transform Anchor;
        public readonly ScenarioRuleLibrary.CActor Actor;
        public readonly GloomhavenVR.Hands.HandSide Side;
        public readonly long Seq; // grab order — lowest is PRIMARY (real panel)
    }

    // Index by HandSide (Left=0, Right=1); null = that hand holds nothing.
    private static readonly HeldReg?[] _reg = new HeldReg?[2];
    private static long _seqCounter;

    // Derived each reconcile: PRIMARY (real panel) + SECONDARY (clone panel) docking state.
    private static Transform? _heldAnchor;
    private static ScenarioRuleLibrary.CActor? _heldActor;
    private static float _heldSideSign = 1f;
    private static Transform? _held2Anchor;
    private static ScenarioRuleLibrary.CActor? _held2Actor;
    private static float _held2SideSign = 1f;

    // The second, cloned ActorStatPanel (created lazily when a second hand holds a figure). Persists
    // hidden between uses; never Destroyed at runtime (its Singleton-base OnDestroy would null the
    // game's real instance) — only torn down at Shutdown with the real instance restored after.
    private static ActorStatPanel? _clone;
    private static ActorStatPanel? _realPanel; // last-known REAL singleton, for singleton self-heal
    private readonly Watch _clonePanel = new();

    /// <summary>Viewer-relative dock side for a hand: RIGHT hand → viewer-LEFT (-1), LEFT hand →
    /// viewer-RIGHT (+1), so the holding hand never occludes its own panel (item 5).</summary>
    private static float SignFor(GloomhavenVR.Hands.HandSide side)
        => side == GloomhavenVR.Hands.HandSide.Right ? -1f : 1f;

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
        _clonePanel.OnShown = () => _clonePanel.PendingShow = true;
        _clonePanel.OnHidden = () => ScheduleRelease(_clonePanel);

        // P5 (MISSION A.8): Board announces miniature pokes on the bus; opening the
        // game's own ActorStatPanel window here triggers the conversion above via its
        // UIWindow onShown — the presentation pipeline is unchanged.
        VREvents.MiniaturePoked += OnMiniaturePoked;
    }

    /// <summary>
    /// TASK #4: register a figure held in <paramref name="holdingHand"/> so its info panel docks at
    /// that hand. The FIRST-held hand takes the game's real ActorStatPanel; a SECOND-held hand takes
    /// a cloned panel. <see cref="Reconcile"/> then (re)binds both panels. Replaces the old single
    /// static held anchor.
    /// </summary>
    internal static void ShowHeldFigure(Transform anchor, ScenarioRuleLibrary.CActor? actor,
        GloomhavenVR.Hands.HandSide holdingHand)
    {
        if (actor == null || anchor == null)
            return;
        _reg[(int)holdingHand] = new HeldReg(anchor, actor, holdingHand, ++_seqCounter);
        Reconcile();
    }

    /// <summary>
    /// TASK #4: clear the held registration for <paramref name="holdingHand"/> (only if
    /// <paramref name="actor"/> still owns that slot) and re-bind the panels. If the OTHER hand still
    /// holds, its figure is promoted to the real panel and the clone hidden; if neither hand holds,
    /// both panels are hidden.
    /// </summary>
    internal static void ClearHeldFigure(GloomhavenVR.Hands.HandSide holdingHand,
        ScenarioRuleLibrary.CActor? actor)
    {
        int i = (int)holdingHand;
        HeldReg? cur = _reg[i];
        if (cur.HasValue && actor != null && !ReferenceEquals(cur.Value.Actor, actor))
            return; // that hand already re-registered a different figure
        ScenarioRuleLibrary.CActor? released = cur?.Actor ?? actor;
        _reg[i] = null;
        Reconcile(released);
    }

    /// <summary>
    /// Re-open the held figures' stat cards if the game's board hover re-targeted the shared real
    /// panel to another actor (risk #5). Cheap; called every frame while any figure is held.
    /// </summary>
    internal static void ReassertHeld()
    {
        if (CanvasConversion.IsLockedNow)
            return;
        Reconcile();
    }

    /// <summary>
    /// Derive PRIMARY (real panel) and SECONDARY (clone panel) from the per-hand registrations and
    /// bind each panel to its figure. PRIMARY = the earliest-grabbed hand (so one hold is unchanged).
    /// </summary>
    private static void Reconcile(ScenarioRuleLibrary.CActor? releasing = null)
    {
        HeldReg? primary = null, secondary = null;
        for (int i = 0; i < _reg.Length; i++)
        {
            HeldReg? r = _reg[i];
            if (!r.HasValue)
                continue;
            if (!primary.HasValue || r.Value.Seq < primary.Value.Seq)
            {
                secondary = primary;
                primary = r;
            }
            else
            {
                secondary = r;
            }
        }

        _heldActor = primary?.Actor;
        _heldAnchor = primary?.Anchor;
        _heldSideSign = primary.HasValue ? SignFor(primary.Value.Side) : 1f;
        _held2Actor = secondary?.Actor;
        _held2Anchor = secondary?.Anchor;
        _held2SideSign = secondary.HasValue ? SignFor(secondary.Value.Side) : 1f;

        bool canShow = WorldUIConfig.StatPanels.Value && WorldUIConfig.ConversionActive
                       && !CanvasConversion.IsLockedNow && Singleton<ActorStatPanel>.IsInitialized;

        // --- real panel (primary) ---
        if (canShow && _heldActor != null)
            ForceShowOn(ActorStatPanel.Instance, _heldActor);
        else if (_heldActor == null && Singleton<ActorStatPanel>.IsInitialized)
            HideHeldOn(ActorStatPanel.Instance, releasing); // no primary hold → drop our card if still ours

        // --- clone panel (secondary) ---
        if (canShow && _held2Actor != null)
        {
            EnsureClone();
            if (_clone != null)
                ForceShowOn(_clone, _held2Actor);
        }
        else if (_held2Actor == null && _clone != null)
        {
            HideHeldOn(_clone, releasing); // no secondary hold → hide the clone card
        }
    }

    /// <summary>
    /// Show <paramref name="actor"/> in <paramref name="panel"/>, RETARGETING if needed. The game's
    /// <c>ActorStatPanel.Show</c> is gated by <c>CanShow()</c> (refuses while another actor is shown),
    /// so we clear the latch first. No-op when the right actor is already shown. Works on the real
    /// panel AND on the clone (Show is an instance method that does not touch the Singleton accessor).
    /// </summary>
    private static void ForceShowOn(ActorStatPanel panel, ScenarioRuleLibrary.CActor actor)
    {
        if (panel == null || ReferenceEquals(panel.m_ActorShown, actor))
            return;
        if (panel.m_ActorShown != null)
            panel.HideForActor(); // clear m_ActorShown so CanShow() passes and Show() retargets
        panel.Show(actor);
    }

    /// <summary>Hide the held card on <paramref name="panel"/> if it still shows a held figure the
    /// game did not re-target (mirrors the old ClearHeldFigure nuance).</summary>
    private static void HideHeldOn(ActorStatPanel panel, ScenarioRuleLibrary.CActor? onlyIf)
    {
        if (panel == null)
            return;
        ScenarioRuleLibrary.CActor? shown = panel.m_ActorShown;
        if (shown == null)
            return;
        // Only hide OUR held figures — never a panel the game re-targeted to a board-hover actor.
        bool ours = ReferenceEquals(shown, _heldActor) || ReferenceEquals(shown, _held2Actor)
                    || (onlyIf != null && ReferenceEquals(shown, onlyIf))
                    || ReferenceEquals(panel, _clone); // the clone is ONLY ever our held figure
        if (ours)
            panel.HideForActor(shown);
    }

    /// <summary>
    /// Lazily Instantiate a SECOND ActorStatPanel bound to the second held figure. The clone's
    /// Singleton-base Awake transiently steals the game's <c>_instance</c>; we restore it to the real
    /// panel immediately (and again defensively in <see cref="HealSingleton"/>). The clone is parented
    /// under the same canvas as the real panel so uGUI renders it until <see cref="TickWatch"/>
    /// converts it to a world host. Never destroyed at runtime (its OnDestroy would null the real
    /// instance) — persists hidden and is reused.
    /// </summary>
    private static void EnsureClone()
    {
        if (_clone != null || !Singleton<ActorStatPanel>.IsInitialized)
            return;
        ActorStatPanel real = ActorStatPanel.Instance;
        if (real == null)
            return;

        var go = UnityEngine.Object.Instantiate(real.gameObject);
        go.name = "GloomhavenVR.ActorStatPanelClone";
        _clone = go.GetComponent<ActorStatPanel>();

        // The clone's Awake set the Singleton to itself — put the game's real panel back.
        real.SetInstance(real);
        _realPanel = real;

        go.transform.SetParent(real.transform.parent, worldPositionStays: false);
        if (_clone != null)
            _clone.HideForActor(); // start hidden; Reconcile shows it for the secondary figure

        VRLog.Info("WorldUI", "second held-figure info panel created (cloned ActorStatPanel) — both " +
                              "hands' figures now show a panel each.");
    }

    /// <summary>
    /// Keep the game's <c>Singleton&lt;ActorStatPanel&gt;.Instance</c> pointing at the REAL panel. The
    /// clone's Singleton-base Awake/OnDestroy can transiently steal or null it; this re-asserts the
    /// real instance. Cheap; only meaningful once a clone exists.
    /// </summary>
    private static void HealSingleton()
    {
        if (_clone == null)
            return;
        ActorStatPanel? live = Singleton<ActorStatPanel>.IsInitialized ? ActorStatPanel.Instance : null;
        if (live != null && !ReferenceEquals(live, _clone))
        {
            _realPanel = live; // the genuine game instance
            return;
        }
        // Singleton is null or was stolen by the clone — restore the cached real panel.
        if (_realPanel != null)
            _realPanel.SetInstance(_realPanel);
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
        HealSingleton(); // keep the game's ActorStatPanel singleton pointing at the real panel

        TickWatch(_actorPanel,
            Singleton<ActorStatPanel>.IsInitialized ? Singleton<ActorStatPanel>.Instance : null,
            "ActorStatPanel");
        TickWatch(_enemyTurnPanel,
            Singleton<EnemyCurrentTurnStatPanel>.IsInitialized ? Singleton<EnemyCurrentTurnStatPanel>.Instance : null,
            "EnemyCurrentTurnStatPanel");
        // TASK #4 — the cloned second panel (only live once a second hand held a figure). Uses the
        // SAME convert/place/billboard machinery; PlaceWatch docks it at the secondary hand.
        TickWatch(_clonePanel, _clone != null ? _clone : null, "ActorStatPanelClone");
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

        // Held-figure override (P8, per hand): if THIS watch shows a figure currently held in EITHER
        // hand, anchor beside that held mini (tighter offset so it clears the hand) on the side
        // opposite that hand — instead of its board cell. Billboarded to the player either way.
        bool held = TryGetHeldFor(WatchActor(watch), out Transform? heldAnchor, out float heldSign);
        Vector3? anchor = held ? heldAnchor!.position : ResolveActorAnchor(watch);
        if (anchor.HasValue && head != null)
        {
            // Held: dock on the side OPPOSITE the holding hand (item 5) via the per-hand sign so the
            // hand never occludes the panel; the board case stays on the fixed viewer-right side.
            float sideOffset = held ? HeldSideOffset * heldSign : 0.30f;
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

    /// <summary>Per-hand held-dock lookup: if <paramref name="actor"/> is a figure currently held in
    /// either hand, return that hand's anchor + viewer-side sign. Matches both the PRIMARY (real
    /// panel) and SECONDARY (clone panel) held figures.</summary>
    private static bool TryGetHeldFor(ScenarioRuleLibrary.CActor? actor, out Transform? anchor, out float sign)
    {
        if (actor != null && _heldAnchor != null && ReferenceEquals(actor, _heldActor))
        {
            anchor = _heldAnchor;
            sign = _heldSideSign;
            return true;
        }
        if (actor != null && _held2Anchor != null && ReferenceEquals(actor, _held2Actor))
        {
            anchor = _held2Anchor;
            sign = _held2SideSign;
            return true;
        }
        anchor = null;
        sign = 1f;
        return false;
    }

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
        DetachWatch(_clonePanel);
        DestroyClone();
        for (int i = 0; i < _reg.Length; i++)
            _reg[i] = null;
        _heldActor = null; _heldAnchor = null; _held2Actor = null; _held2Anchor = null;
    }

    /// <summary>
    /// Tear the cloned second panel down and RESTORE the game's real Singleton. The clone's
    /// Singleton-base OnDestroy nulls <c>_instance</c> — so we DestroyImmediate it (OnDestroy runs
    /// synchronously) then re-assert the real panel. Guarded so a scene teardown that already killed
    /// the real panel doesn't resurrect a dead reference.
    /// </summary>
    private static void DestroyClone()
    {
        if (_clone == null)
            return;

        ActorStatPanel? real = _realPanel;
        if ((real == null || ReferenceEquals(real, _clone)) && Singleton<ActorStatPanel>.IsInitialized
            && !ReferenceEquals(ActorStatPanel.Instance, _clone))
            real = ActorStatPanel.Instance;

        GameObject go = _clone.gameObject;
        _clone = null;
        UnityEngine.Object.DestroyImmediate(go); // OnDestroy nulls the singleton synchronously here

        if (real != null) // real still alive → put it back as the game singleton
            real.SetInstance(real);
        _realPanel = null;
    }
}
