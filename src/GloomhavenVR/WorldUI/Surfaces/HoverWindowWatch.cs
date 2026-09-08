using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// <b>ONE HOVER-DRIVEN GAME WINDOW, WATCHED — the show/hide listeners, the release HYSTERESIS and
/// the churn alarm that go with it.</b> The state and the mechanism; the surface that owns the
/// instance decides what to CONVERT and where to put it.
///
/// <para><b>WHY IT EXISTS, AND WHY THE 2026-07 VERDICT IS NOT BEING OVERTURNED (refactor 2026-09,
/// REVIEW-worldui-front.md F2).</b> <see cref="PropInfoSurface"/> and <see cref="StatPanelSurface"/>
/// carried this same state class, the same three constants, the same <c>ScheduleRelease</c>,
/// <c>CountConversion</c>, <c>RescanMips</c>, <c>Release</c> and <c>DetachWatch</c> — the biggest
/// verbatim duplication in the whole subsystem (the census's 58-line group, and the only one the
/// 2026-07 review found worth naming). That review considered merging them and ruled against, on
/// one argument: <i>"the constants are PER-SURFACE TUNABLES … and a shared core would have to take
/// all three as arguments, which is two call sites with the same three numbers rather than one
/// implementation."</i></para>
///
/// <para><b>That is exactly what this is, and it is the right shape rather than the compromise the
/// verdict feared.</b> The three numbers are CONSTRUCTOR ARGUMENTS, declared as consts on the
/// surface that owns them, so a future round can retune one surface's hysteresis without touching
/// the other — which is the property the verdict was protecting. What stops being duplicated is the
/// MECHANISM, which is the thing that can silently drift. The registry's own warning
/// ([[a-frozen-literal-is-a-consumer]] and standing warning #3, "three near-identical constants are
/// usually three different bugs") is about the NUMBERS, and the numbers are still three-per-surface.
/// The user's standing preference points the same way: <i>"versuche Redundanz im Code zu vermeiden,
/// alles nochmal neu für die Props zu schreiben obwohl doch das meiste schon für die Figuren gebaut
/// wurde und funktioniert"</i>.</para>
///
/// <para><b>WHAT IS DELIBERATELY NOT HERE.</b> The <c>TickWatch</c> body of each surface, because
/// the two genuinely differ and each difference is load-bearing: <see cref="StatPanelSurface"/>
/// converts at <c>StatPanelSortingOrder</c> (10 — a named invariant, "a deliberate middle tier")
/// and additionally gates on <c>Choreographer.s_Choreographer != null</c>;
/// <see cref="PropInfoSurface"/> converts at the default order and has no such gate. The PLACEMENT
/// (<c>PlaceWatch</c>) is per-surface for the same reason — a stat card docks beside the miniature,
/// a prop card at the PropInfo layout slot. Both are handed this record and read its fields.</para>
///
/// <para><b>THE HYSTERESIS IS THE WHOLE POINT (test #16 pattern).</b> The game's hover logic hides
/// and re-shows these windows on every hover change; releasing instantly would re-parent a whole
/// uGUI subtree at sweep rate. A re-show inside the window cancels the pending release instead. The
/// churn warning exists so that a FUTURE loop of this class is attributable from a hardware log
/// alone — it is not decoration, and its text is a log grep token.</para>
///
/// <para><b>MULTIPLAYER:</b> nothing here is networked. A hover happens on the machine whose pointer
/// is hovering, and these windows are the game's own local UI.</para>
/// </summary>
internal sealed class HoverWindowWatch
{
    private readonly float _releaseDelaySeconds;
    private readonly float _churnWindowSeconds;
    private readonly int _churnWarnCount;
    private readonly float _mipRescanInterval;

    /// <param name="releaseDelaySeconds">Hide→release hysteresis, unscaled seconds.</param>
    /// <param name="churnWindowSeconds">Rolling window for the conversion counter.</param>
    /// <param name="churnWarnCount">Conversions inside one window above which the single warning fires.</param>
    /// <param name="mipRescanInterval">Mip-bake rescan cadence while the panel is converted.</param>
    internal HoverWindowWatch(float releaseDelaySeconds, float churnWindowSeconds,
                              int churnWarnCount, float mipRescanInterval)
    {
        _releaseDelaySeconds = releaseDelaySeconds;
        _churnWindowSeconds = churnWindowSeconds;
        _churnWarnCount = churnWarnCount;
        _mipRescanInterval = mipRescanInterval;
        OnShown = () => PendingShow = true;
        OnHidden = ScheduleRelease;
    }

    /// <summary>The game component this watch is currently attached to, or null.</summary>
    public Component? Attached;

    /// <summary>The <c>UIWindow</c> on <see cref="Attached"/>, i.e. what the listeners hang off.</summary>
    public UIWindow? Window;

    /// <summary>The live conversion, or null while the window is not converted.</summary>
    public ConvertedPanel? Panel;

    /// <summary>The game showed the window and the surface has not acted on it yet.</summary>
    public bool PendingShow;

    /// <summary>True for the <c>UITextInfoPanel</c> watch, false for the <c>UIPropInfoPanel</c>
    /// one. Read only by <see cref="PropInfoSurface"/>; <see cref="StatPanelSurface"/> leaves it
    /// false for both of its watches.
    ///
    /// <para>Until ModBuild 366 this flag also answered "may this watch dock at a hand?", because
    /// <c>UITextInfoPanel</c> was the ONE window a held prop could raise. ModBuild 366 moved the
    /// held card to the RICH window whenever the prop has one (the user wants "immer die
    /// detaillierteste Info ... inkl. aller effekte"), so the two questions came apart: this stays
    /// the WINDOW discriminator and <c>HeldPropCard.Owns</c> answers the held one. Leaving the dock
    /// keyed on this flag would have fixed the content and lost the position.</para></summary>
    public bool IsTextInfo;

    /// <summary>Unscaled time at which a scheduled release fires; 0 = none pending.</summary>
    public float ReleaseAt;

    // Churn telemetry (test #16 pattern): conversions inside the rolling window.
    public float CycleWindowStart;
    public int CycleCount;
    public bool ChurnWarned;

    /// <summary>Unscaled time of the next mip-bake rescan for this panel.</summary>
    public float NextMipRescan;

    /// <summary>The listeners this watch adds to <see cref="Window"/>. Built once, in the
    /// constructor, so the ADD and the REMOVE are provably the same delegate instance — a listener
    /// removed by a freshly allocated lambda is not removed at all.</summary>
    public readonly UnityEngine.Events.UnityAction OnShown;

    /// <inheritdoc cref="OnShown"/>
    public readonly UnityEngine.Events.UnityAction OnHidden;

    /// <summary>
    /// Re-point the watch at <paramref name="live"/> when it has changed: detach from the old
    /// window (which releases any conversion), take the new one, subscribe, and arm
    /// <see cref="PendingShow"/> if it is already open. A no-op when nothing changed.
    /// </summary>
    /// <returns>true when the attachment changed.</returns>
    internal bool Attach(Component? live)
    {
        if (ReferenceEquals(live, Attached))
            return false;
        Detach();
        Attached = live;
        Window = live != null ? live.GetComponent<UIWindow>() : null;
        if (Window != null)
        {
            Window.onShown.AddListener(OnShown);
            Window.onHidden.AddListener(OnHidden);
            if (Window.IsOpen)
                PendingShow = true;
        }
        return true;
    }

    /// <summary>
    /// Hide → deferred release (test #16 pattern). The game's hover logic hides / re-shows the
    /// panel on every hover change; releasing instantly would re-parent the whole uGUI subtree at
    /// sweep rate. A re-show within the window cancels the pending release.
    /// </summary>
    internal void ScheduleRelease()
    {
        PendingShow = false;
        if (Panel != null)
            ReleaseAt = Time.unscaledTime + _releaseDelaySeconds;
    }

    /// <summary>True when a scheduled release has come due.</summary>
    internal bool ReleaseDue => Panel != null && ReleaseAt > 0f && Time.unscaledTime >= ReleaseAt;

    /// <summary>One warning if a panel still churns through conversions (test #16).</summary>
    internal void CountConversion(string name)
    {
        float now = Time.unscaledTime;
        if (now - CycleWindowStart > _churnWindowSeconds)
        {
            CycleWindowStart = now;
            CycleCount = 0;
        }
        CycleCount++;
        if (CycleCount > _churnWarnCount && !ChurnWarned)
        {
            ChurnWarned = true;
            VRLog.Warn("WorldUI", $"{name} convert/release churn: >{_churnWarnCount} conversions in " +
                                  $"{_churnWindowSeconds:F0}s despite the {_releaseDelaySeconds:F1}s release " +
                                  "hysteresis — something still occludes/toggles the window per frame.");
        }
    }

    /// <summary>
    /// One cadence-gated mip-bake pass over the converted panel. Config-gated and fully guarded
    /// inside <see cref="PanelMipBake.Rescan"/> (a bake surprise can never break a surface's tick),
    /// and idempotent-cheap once warm — a graphic already wearing a baked sprite resolves to a
    /// dictionary hit and is not rewritten.
    ///
    /// <para>Scans the GAME widget root, not the host: it is the exact subtree <c>Convert</c>
    /// reparented, and it stays the right root in both states — which is what lets
    /// <see cref="Release"/> use the same handle after the content has gone home.</para>
    /// </summary>
    internal void RescanMips(string name)
    {
        if (Panel == null || Attached == null || Time.unscaledTime < NextMipRescan)
            return;
        NextMipRescan = Time.unscaledTime + _mipRescanInterval;
        PanelMipBake.Rescan(Attached, name);
    }

    /// <summary>
    /// Release the conversion and re-arm. Mutate-and-restore house style: the originals go back
    /// BEFORE the subtree returns to the 2D UI, so the game's own screen-space panel is left
    /// exactly as authored (the baked copies are a VR presentation detail).
    /// </summary>
    internal void Release()
    {
        PendingShow = false;
        ReleaseAt = 0f;
        if (Panel != null)
        {
            PanelMipBake.Restore(Attached);
            CanvasConversion.Release(Panel);
            Panel = null;
        }
        NextMipRescan = 0f; // a fresh conversion rescans immediately
    }

    /// <summary>
    /// Drop the listeners, release the conversion and forget the attachment.
    ///
    /// <para>RELEASE BEFORE DROPPING <see cref="Attached"/>: the release path restores the original
    /// sprites through that very handle (<see cref="PanelMipBake.Restore"/>), so nulling it first
    /// would silently leave our baked copies on the game's 2D panel.</para>
    /// </summary>
    internal void Detach()
    {
        if (Window != null)
        {
            Window.onShown.RemoveListener(OnShown);
            Window.onHidden.RemoveListener(OnHidden);
        }
        Window = null;
        Release();
        Attached = null;
    }
}
