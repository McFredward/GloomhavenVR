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
                Panel = CanvasConversion.Convert(target, Name);
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

    public virtual void Shutdown()
    {
        if (Panel != null)
        {
            CanvasConversion.Release(Panel);
            Panel = null;
        }
    }
}
