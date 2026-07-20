using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// Subtle PRE-GRAB proximity highlight for a board figure: a warm amber-gold EMISSIVE glow
/// applied to the figure's OWN renderer materials so the player can see which figure their
/// hand would pluck before they grab it.
///
/// Why the figure's own material (and not the game's <c>ActorBehaviour.m_Hilight</c> ring)?
/// m_Hilight draws ZTest Always by design, so it punches THROUGH walls and floors. This mod
/// renders Forward and wants the highlight occluded like the figure itself. Because this glow
/// lives on the figure's own material it uses that shader's native ZTest (LEqual) and is
/// therefore hidden by walls exactly like the mini — occlusion-correct by construction.
///
/// Mechanics mirror the renderer-snapshot approach already used in
/// <see cref="FigureGrabbable"/>: snapshot each renderer's ORIGINAL <c>sharedMaterials</c>,
/// swap in per-renderer INSTANCE materials (so sibling figures of the same class are never
/// affected — no global shared-material mutation), enable <c>_EMISSION</c> and set a subtle
/// <c>_EmissionColor</c>. On clear we restore the shared materials verbatim AND destroy the
/// temporary instances, so nothing leaks across repeated hovers or on figure teardown.
/// </summary>
internal sealed class FigureHighlight
{
    private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");
    private const string EmissionKeyword = "_EMISSION";

    // Warm amber-gold — the candle-lit-dungeon palette of Gloomhaven. Bright enough to read
    // as "this is the one", dim enough not to blow the mini out. Emissive (self-lit glow),
    // NOT an albedo tint. Tunable: raise/lower the multiplier if it reads too hot/cold on device.
    private static readonly Color GlowColor = new Color(1.0f, 0.74f, 0.36f) * 0.75f;

    private Renderer[]? _renderers;
    private Material[][]? _origSharedMats;
    private Material[][]? _instanceMats;

    /// <summary>True while the glow is applied (snapshot held).</summary>
    public bool Active => _renderers != null;

    /// <summary>
    /// Apply the emissive glow to <paramref name="root"/>'s renderers. Returns true if at least
    /// one material accepted the emission (so the caller can log a graceful SKIP when none do,
    /// rather than fall back to a see-through ring). No-op (returns true) if already active.
    /// </summary>
    public bool Apply(GameObject root)
    {
        if (_renderers != null)
            return true; // already glowing

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        var origShared = new Material[renderers.Length][];
        var instances = new Material[renderers.Length][];
        bool any = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;
            origShared[i] = r.sharedMaterials;   // snapshot the ORIGINAL shared assets
            Material[] mats = r.materials;        // per-renderer INSTANCES (r now owns these copies)
            instances[i] = mats;
            foreach (Material mat in mats)
            {
                if (mat == null || !mat.HasProperty(EmissionColorProp))
                    continue;
                mat.EnableKeyword(EmissionKeyword);
                mat.SetColor(EmissionColorProp, GlowColor);
                any = true;
            }
        }

        if (!any)
        {
            // No emissive-capable material — undo the instance swap and destroy the copies so we
            // neither leak nor alter the figure. Caller shows no highlight (avoids a see-through ring).
            RestoreAndDestroy(renderers, origShared, instances);
            return false;
        }

        _renderers = renderers;
        _origSharedMats = origShared;
        _instanceMats = instances;
        return true;
    }

    /// <summary>
    /// Restore the original shared materials and destroy the temporary instances (idempotent).
    /// Safe to call after the figure was torn down (Unity-fake-null renderers are skipped).
    /// </summary>
    public void Clear()
    {
        if (_renderers == null)
            return;
        RestoreAndDestroy(_renderers, _origSharedMats!, _instanceMats!);
        _renderers = null;
        _origSharedMats = null;
        _instanceMats = null;
    }

    private static void RestoreAndDestroy(Renderer[] renderers, Material[][] origShared, Material[][] instances)
    {
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r != null && origShared[i] != null)
                r.sharedMaterials = origShared[i]; // back to the shared assets → instances orphaned

            Material[]? inst = instances[i];
            if (inst == null)
                continue;
            foreach (Material m in inst)
            {
                if (m != null)
                    Object.Destroy(m); // free the temp instance so nothing leaks per hover
            }
        }
    }
}
