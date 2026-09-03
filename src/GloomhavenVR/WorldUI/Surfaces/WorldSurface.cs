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

    public virtual void Init() { }

    public virtual void Tick()
    {
        // Scene unload killed the target — the framework pruned the host already.
        if (Panel != null && !Panel.IsAlive)
            Panel = null;

        bool want = WantConverted;
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
    /// <para><b>THE DEFAULT IS BYTE-FOR-BYTE WHAT EVERY CALL SITE USED TO DO</b> — one
    /// <c>CanvasConversion.Release</c> — so nothing about any surface changes by this existing. It
    /// is <c>virtual</c> for exactly one override today: <see cref="FloatingDecisionSurface"/> hands
    /// the release to a running vanish animation when one is in flight, and falls straight through
    /// to this body when one is not, which is every other case.</para>
    ///
    /// <para><b>AN OVERRIDE MAY DEFER THE RELEASE BUT MAY NEVER DROP IT.</b> The caller sets
    /// <c>Panel = null</c> in the same statement, so from this line on nothing else in the framework
    /// holds the conversion: an override that swallowed it would leave the game's window parented
    /// under a mod host with no owner. That is the same contract
    /// <c>WindowMaterialise.PlayOut</c> states for its own callback, and it is why the one override
    /// routes through a method whose totality is documented rather than through a timer.</para>
    /// </summary>
    protected virtual void ReleasePanel(ConvertedPanel panel) => CanvasConversion.Release(panel);
}
