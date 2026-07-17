using GloomhavenVR.Core;
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
            CanvasConversion.Release(Panel);
            Panel = null;
        }

        if (Panel != null)
            Place();
    }

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
        CanvasConversion.Release(Panel);
        Panel = null;
        return true;
    }

    public virtual void Shutdown()
    {
        if (Panel != null)
        {
            CanvasConversion.Release(Panel);
            Panel = null;
        }
    }
}
