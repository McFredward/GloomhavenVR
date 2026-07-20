using UnityEngine;
using Object = UnityEngine.Object;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// Subtle PRE-GRAB proximity highlight for a board figure so the player can see which mini their
/// hand would pluck before they grab it.
///
/// ROBUSTNESS (task #5): the board figures render with the game's CUSTOM Amplify character shaders
/// (<c>Amp_CharShader_Low</c>, <c>Amp_Char_Shader_Side_Low</c>, <c>Amp_CharDistort_Low</c>, … —
/// confirmed from the always-loaded shader manifest in Player.log). Those shaders expose their own
/// property set (<c>_Toggle_Dissolve</c>, <c>_InvisibilityControl</c>, … — see
/// <c>ActorBehaviour.CheckInvisibility</c>) and do NOT reliably carry Unity's Standard
/// <c>_EmissionColor</c> / <c>_EMISSION</c>, so a purely emissive glow silently does nothing on
/// them (the earlier build's SKIP path). To be ROBUST we therefore lead with a SHADER-AGNOSTIC
/// effect: a small uniform SCALE "pop" of the figure's animated visual root. It:
///   (a) is clearly visible — the mini noticeably grows the instant the hand is in reach and snaps
///       back when it leaves, independent of any shader property;
///   (b) never renders through walls — it reuses the figure's OWN renderers, so it inherits the
///       Amp shader's native ZTest (LEqual) and is occluded by terrain exactly like the mini
///       (unlike the game's <c>m_Hilight</c> ring, which draws ZTest Always and shows through
///       walls — the reason that path was abandoned, LogOutput.log);
///   (c) snapshots / restores cleanly — one <c>localScale</c> Vector3, no allocations, no created
///       objects, nothing to leak, safe after actor teardown (Unity fake-null guarded);
///   (d) is driven purely by the single-winner <c>OnGrabHighlight</c> callback (no per-frame tick).
///
/// The scale target is the actor's <c>m_AnimatedGameObject</c> (the animator root that parents the
/// renderers), NOT the actor root: the game's <c>ActorBehaviour</c> writes the root's position and
/// rotation every frame but NEVER its scale (verified, DoTransform/ApplyMotion), and it only zeroes
/// the animated child's <c>localPosition</c> — never its scale — so scaling the animated child is
/// fought by nothing and cannot perturb the root collider used for grab proximity.
///
/// On top of the scale pop we ALSO apply the warm amber-gold EMISSIVE glow when — and only when —
/// the figure's shader actually carries <c>_EmissionColor</c> (pre-scanned on the SHARED materials,
/// so the common unsupported case allocates nothing). When supported it's a nicer candle-lit accent;
/// when not, the scale pop alone still reads clearly.
/// </summary>
internal sealed class FigureHighlight
{
    private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");
    private const string EmissionKeyword = "_EMISSION";

    // Warm amber-gold — the candle-lit-dungeon palette of Gloomhaven. Emissive (self-lit glow),
    // applied only when the shader supports it. Tunable multiplier.
    private static readonly Color GlowColor = new Color(1.0f, 0.74f, 0.36f) * 0.75f;

    // Uniform "pop": grow the mini ~12% while the hand is in reach. Big enough to read instantly,
    // small enough never to overlap the neighbouring hex's figure. Tunable.
    private const float ScaleFactor = 1.12f;

    private Transform? _scaleTarget;
    private Vector3 _origScale;

    private Renderer[]? _renderers;
    private Material[][]? _origSharedMats;
    private Material[][]? _instanceMats;
    private bool _emissionEngaged;

    /// <summary>True while the highlight is applied (snapshot held).</summary>
    public bool Active => _scaleTarget != null || _renderers != null;

    /// <summary>True when the (bonus) emissive glow engaged — i.e. the figure's shader carried
    /// <c>_EmissionColor</c>. The shader-agnostic scale pop engages regardless.</summary>
    public bool EmissionEngaged => _emissionEngaged;

    /// <summary>
    /// Apply the highlight. <paramref name="scaleTarget"/> is the visual transform to pop (the
    /// actor's animated root); <paramref name="root"/> owns the renderers scanned for the optional
    /// emissive glow. Returns true if the emissive glow ALSO engaged (for richer logging); the
    /// scale pop always engages. No-op if already active.
    /// </summary>
    public bool Apply(GameObject root, Transform? scaleTarget)
    {
        if (Active)
            return _emissionEngaged;

        // (1) Shader-agnostic, occlusion-correct scale pop — the guaranteed-visible primary effect.
        if (scaleTarget != null)
        {
            _scaleTarget = scaleTarget;
            _origScale = scaleTarget.localScale;
            scaleTarget.localScale = _origScale * ScaleFactor;
        }

        // (2) Bonus emissive glow — ONLY if the figure's shader actually carries _EmissionColor.
        // Pre-scan the SHARED materials first so the common (Amp char shader) unsupported case
        // allocates zero instance materials.
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        bool supportsEmission = false;
        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;
            foreach (Material sm in r.sharedMaterials)
            {
                if (sm != null && sm.HasProperty(EmissionColorProp))
                {
                    supportsEmission = true;
                    break;
                }
            }
            if (supportsEmission)
                break;
        }

        if (supportsEmission)
        {
            var origShared = new Material[renderers.Length][];
            var instances = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null)
                    continue;
                origShared[i] = r.sharedMaterials; // snapshot the ORIGINAL shared assets
                Material[] mats = r.materials;      // per-renderer INSTANCES (r now owns these copies)
                instances[i] = mats;
                foreach (Material mat in mats)
                {
                    if (mat == null || !mat.HasProperty(EmissionColorProp))
                        continue;
                    mat.EnableKeyword(EmissionKeyword);
                    mat.SetColor(EmissionColorProp, GlowColor);
                }
            }
            _renderers = renderers;
            _origSharedMats = origShared;
            _instanceMats = instances;
            _emissionEngaged = true;
        }

        return _emissionEngaged;
    }

    /// <summary>
    /// Restore the original scale and (if applied) the original shared materials, destroying the
    /// temporary instances (idempotent). Safe after the figure was torn down (Unity fake-null
    /// renderers/transforms are skipped).
    /// </summary>
    public void Clear()
    {
        if (_scaleTarget != null)
        {
            if (_scaleTarget) // Unity fake-null: skip a destroyed transform
                _scaleTarget.localScale = _origScale;
            _scaleTarget = null;
        }

        if (_renderers != null)
        {
            RestoreAndDestroy(_renderers, _origSharedMats!, _instanceMats!);
            _renderers = null;
            _origSharedMats = null;
            _instanceMats = null;
        }

        _emissionEngaged = false;
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
