using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Base class for a physicalized UI surface: watches its config toggle and the
/// conversion gate, converts its target panel via <see cref="CanvasConversion"/>
/// when it becomes available, releases it (restoring the 2D UI) when the gate
/// closes, and re-places the host every tick (allocation-free).
/// </summary>
internal abstract class WorldSurface
{
    protected ConvertedPanel? Panel;

    public abstract string Name { get; }

    /// <summary>Live config toggle for this surface.</summary>
    protected abstract bool ConfigEnabled { get; }

    /// <summary>The panel to move to world space (null while not present).</summary>
    protected abstract RectTransform? FindTarget();

    /// <summary>
    /// Opt-in (test #21): neutralize the game's real 3D styling (local rotations /
    /// z offsets) inside the converted subtree — see
    /// <see cref="CanvasConversion.FlattenSubtree"/>. Default off: only surfaces
    /// with a VERIFIED tilt symptom flatten (combat log today).
    /// </summary>
    protected virtual bool Flatten2D => false;

    /// <summary>Position the host in the world (called every tick while converted).</summary>
    protected abstract void Place();

    /// <summary>Additional gate; default: only inside a scenario.</summary>
    protected virtual bool WantConverted =>
        ConfigEnabled && WorldUIConfig.ConversionActive && Choreographer.s_Choreographer != null;

    /// <summary>
    /// <b>DOES THIS SURFACE'S WINDOW DISSOLVE INTO DUST WHEN THE MOD CLOSES IT?</b> Default no.
    /// This is the ONE membership test for the surface family's vanish, and
    /// <see cref="ReleasePanel"/> below is the one place that reads it — so a future window opts in
    /// by overriding this property and cannot then forget the release hand-off, the re-convert
    /// cancel or the shutdown, all of which are owned by this class.
    ///
    /// <para><b>THE DEFECT THAT PUT IT HERE (user, 2026-09-06, item 4):</b> <i>"Kampflog-Fenster hat
    /// keine in Staub verfallen Animation wenn man es schließt - wie jedes andere Fenster auch, soll
    /// das hier auch der Fall sein."</i> The dissolve worked and the combat log was simply not in
    /// the population: <c>SurfaceMaterialise.Arm</c> had exactly one call site, inside
    /// <c>FloatingDecisionSurface.Place</c>, so three decision popups were the whole set and every
    /// other surface took the bare <c>CanvasConversion.Release</c> below. Membership was a CALL
    /// SITE, which is invisible; it is now a property with a census line beside it.</para>
    ///
    /// <para><b>WHY THE DEFAULT IS NO, AND WHY THAT IS NOT THE SAME OMISSION AGAIN.</b> Most
    /// surfaces are not windows — a damage tooltip, a decision dock, the tray-mounted initiative
    /// track and element board appear and leave on hover and on phase edges, many times a minute.
    /// Dust on those is the shape <c>WindowMaterialise.IsHoverCard</c> already refuses by name for
    /// the map-room hover card ("arrives and leaves instantly by design"), and turning it on for
    /// them is a presentation change nobody asked for. The safeguard against a silent omission is
    /// therefore the CENSUS rather than the default: every surface reports itself on its first tick
    /// and the log names both lists, so the next "window X has no animation" is a reading and not a
    /// hardware round.</para>
    /// </summary>
    internal virtual bool DissolvesOnRelease => false;

    /// <summary>
    /// The dissolve currently holding this surface's previous release, if any. A FIELD rather than a
    /// question asked of the effect, for the reason <c>FloatingDecisionSurface</c> stated when it
    /// owned one: at most one conversion is live per surface, so at most one of these is ever
    /// non-null, and every place that must not wait names THIS panel rather than the whole family.
    /// </summary>
    private ConvertedPanel? _vanishing;

    /// <summary>Reported to the census once — see <see cref="DissolvesOnRelease"/>. Not done in the
    /// constructor: <see cref="Name"/> is abstract, and calling it before the subclass's own fields
    /// are initialised is a trap this class does not need to walk into.</summary>
    private bool _censusReported;

    /// <summary>
    /// Put this surface in the dissolve census, once.
    ///
    /// <para><b>CALLED FROM BOTH <see cref="Tick"/> AND <see cref="ReleasePanel"/>, and the second
    /// one is the one that makes the census COMPLETE rather than merely long.</b> A subclass may
    /// fully override <c>Tick</c> without chaining — <c>DamagePreviewSurface</c> does, and correctly:
    /// its <c>FindTarget</c> returns null, it converts nothing and it has no release edge at all, so
    /// it is not a window and membership is meaningless for it. But a FUTURE surface could override
    /// <c>Tick</c> the same way and still convert, and a census that quietly missed it would be the
    /// exact failure this whole change exists to end. Reporting from the release seam as well makes
    /// the guarantee the one that matters: <b>every surface that ever releases a panel is named in
    /// the census</b>, whatever it does with <c>Tick</c>.</para>
    /// </summary>
    private void ReportCensus()
    {
        if (_censusReported)
            return;
        _censusReported = true;
        SurfaceMaterialise.NoteMembership(Name, DissolvesOnRelease);
    }

    public virtual void Init() { }

    public virtual void Tick()
    {
        ReportCensus();

        // Scene unload killed the target — the framework pruned the host already.
        if (Panel != null && !Panel.IsAlive)
            Panel = null;

        bool want = WantConverted;
        // A DECORATION MAY NEVER MAKE A WINDOW WAIT FOR ITS SLOT. If this surface wants to convert
        // again while its previous panel's release is still held by a dissolve, the dissolve ends
        // NOW and the release runs inside this call — WindowMaterialise.Cancel's documented contract
        // is that a cancelled vanish still runs its callback, so cancelling the animation can never
        // cancel the release. This also closes the re-convert hazard: converting a target still
        // parented under a dying host would record that host as its 2D home.
        if (_vanishing != null && Panel == null && want)
        {
            SurfaceMaterialise.FinishNow(_vanishing, "the surface is converting again");
            _vanishing = null;
        }
        if (want && Panel == null)
        {
            RectTransform? target = FindTarget();
            if (target != null)
            {
                Panel = CanvasConversion.Convert(target, Name, flatten2D: Flatten2D);
                if (Panel != null)
                    OnConverted();
            }
        }
        else if (!want && Panel != null)
        {
            ReleasePanel(Panel);
            Panel = null;
        }

        if (Panel != null)
            Place();
    }

    /// <summary>
    /// LATE placement pass, driven from <c>WorldUIModule.LateUpdate</c> — i.e. AFTER every
    /// Update-phase transform writer in the process has run. Default: nothing. A surface whose
    /// host sits at a fixed world pose (the floating slot layout, the HMD-anchored fallbacks)
    /// has nothing to correct here, and re-placing a GRABBED window in LateUpdate would fight
    /// its carry — so this is opt-in, and only the surfaces that POSE-FOLLOW a moving anchor
    /// take it.
    ///
    /// ROOT CAUSE the seam exists for (user, hardware MP test: "Die Initiativreihenfolge über dem
    /// board und der Aufgabentext links ziehen immer ein wenig nach wenn man das board hin und her
    /// schleudert. Rechts die piles sind zB wie angewurzelt"): a host docked on a
    /// <see cref="Cards.PlayTray"/> mount is deliberately NOT a child of the tray, so its pose is
    /// a per-frame COPY of the mount's — and a copy is only as fresh as the frame ordering makes
    /// it. The board is carried by <see cref="PanelGrabHandle"/>, a MonoBehaviour that writes the
    /// tray root from <c>Update</c> at the default script execution order, i.e. in NO defined
    /// order relative to <c>WorldUIModule.Update</c>. On every frame the handle's Update happens
    /// to run AFTER the module's, the surfaces copied the board's PREVIOUS-frame pose and the
    /// panel renders one frame behind the board it is bolted to — which, while the board is being
    /// flung around, is precisely the visible drag that was reported. The card piles never showed
    /// it because they are real CHILDREN of the tray root: they inherit the transform, no copy,
    /// no ordering.
    ///
    /// Unity runs EVERY <c>LateUpdate</c> after EVERY <c>Update</c>, so a placement written here
    /// reads the board pose this frame will actually render with — ordering-proof by rule instead
    /// of by luck, and without touching the ownership contract that keeps game-owned canvases out
    /// of the tray hierarchy (see <see cref="TrayMountedPanelSurface.LateTick"/>).
    /// </summary>
    public virtual void LateTick() { }

    protected virtual void OnConverted() { }

    /// <summary>
    /// Release the current conversion WITHOUT the want-gate flipping — for a surface
    /// whose <see cref="FindTarget"/> can change to a DIFFERENT target while
    /// <see cref="WantConverted"/> stays true. The base <see cref="Tick"/> only converts
    /// while <c>Panel == null</c>, so a changed target would otherwise never re-convert
    /// (test #26: the decision dock switches prompts YesNoDialog → DialogPopup →
    /// TakeDamagePanel — the first stuck forever). The caller forces a release here so
    /// the next Tick re-converts the new target. Returns true if a panel was released.
    /// </summary>
    protected bool ReleaseCurrentPanel()
    {
        if (Panel == null)
            return false;
        ReleasePanel(Panel);
        Panel = null;
        return true;
    }

    public virtual void Shutdown()
    {
        // A TEARDOWN MUST NOT DEFER A RELEASE BEHIND AN ANIMATION — WindowMaterialise.CancelAll
        // carries the same rule for the modal windows and for the same reason. Ended BEFORE the
        // release below so the panel this surface still holds is released by that call and not by a
        // callback firing into a torn-down surface.
        SurfaceMaterialise.FinishNow(_vanishing, "the surface is shutting down");
        _vanishing = null;
        if (Panel != null)
        {
            ReleasePanel(Panel);
            Panel = null;
        }
    }

    /// <summary>
    /// GIVE THE GAME'S PANEL BACK TO ITS 2D HOME. The single seam every release in this class goes
    /// through, so a subclass that has something to do at the release edge cannot be defeated by
    /// which of the three transitions reached it.
    ///
    /// <para><b>THE TWO HAND-OFFS LIVE HERE, so no subclass can be defeated by which transition
    /// reached it and no two subclasses can implement them differently.</b>
    /// <list type="number">
    /// <item><b>A vanish the CLOSE EDGE already started</b> — <c>SurfaceCloseEdge</c>'s prefix on the
    /// game's own <c>Hide()</c>, one Update tick earlier, for the decision popups the GAME closes.
    /// The release is handed to the dissolve in flight.</item>
    /// <item><b>A vanish that starts HERE</b>, for a surface whose close IS its release — the combat
    /// log's X runs <c>SetUserVisible(false)</c>, not <c>UIWindow.Escape()</c>, so nothing has been
    /// torn down and the game's window is still active, populated and drawing at this statement.
    /// Gated on <see cref="DissolvesOnRelease"/>, which is the family's single membership test.</item>
    /// </list>
    /// Every other route into a release — a destroyed target, a scene unload, the A/X flat-screen
    /// chord, a popup switch, a config toggle, a shutdown — finds no record and no membership and
    /// takes the base body, so the animation stays strictly ADDITIVE and the pre-existing path is
    /// the default rather than the fallback.</para>
    ///
    /// <para><b>THE RELEASE CANNOT BE LOST OR RUN TWICE.</b> Both
    /// <c>SurfaceMaterialise.HandOffRelease</c> and <c>SurfaceMaterialise.VanishAtRelease</c> return
    /// false — meaning "release right now" — whenever there is no live record to carry it, and true
    /// only after storing it on one whose single completion path runs it. The effect's own
    /// completion is total by <c>WindowMaterialise.PlayOut</c>'s contract: the callback runs at the
    /// end of the dissolve, on a cancel, on a watchdog, on the host being deactivated or destroyed,
    /// and synchronously when the effect cannot run at all.</para>
    ///
    /// <para><b>AN OVERRIDE MAY DEFER THE RELEASE BUT MAY NEVER DROP IT.</b> The caller sets
    /// <c>Panel = null</c> in the same statement, so from this line on nothing else in the framework
    /// holds the conversion: an override that swallowed it would leave the game's window parented
    /// under a mod host with no owner.</para>
    /// </summary>
    protected virtual void ReleasePanel(ConvertedPanel panel)
    {
        ReportCensus();
        ConvertedPanel p = panel;
        void Release()
        {
            if (ReferenceEquals(_vanishing, p))
                _vanishing = null;
            CanvasConversion.Release(p);
        }

        if (SurfaceMaterialise.HandOffRelease(p, Release)
            || (DissolvesOnRelease && SurfaceMaterialise.VanishAtRelease(p, Name, Release)))
        {
            _vanishing = p;
            return;
        }
        CanvasConversion.Release(p);
    }
}
