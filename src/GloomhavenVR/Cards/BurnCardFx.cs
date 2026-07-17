using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Bounds the ONE genuinely world-space effect in the game's card burn/ghost timeline
/// (test #22, symptom 4c-i): the <c>CardSmoke</c> <see cref="ParticleSystem"/> that
/// <c>CardEffects.SpawnParticle</c> pool-spawns as a child of the card
/// (<c>ObjectPool.Spawn(GlobalSettings.Instance.VisualEffects.CardSmoke, base.transform)</c>,
/// CardEffects.cs:747, ref kept in <c>CardEffects._smokeEffect</c>).
///
/// Everything ELSE in the burn timeline is uGUI on the card's own canvas — the
/// dissolve/greyOut/burn material sweep on the face images, the <c>_uiFxOverlay</c>
/// flame overlay, the <c>UIFX_MaterialFX_Control</c> hover-FX quads — so once the face
/// is kept on our correctly-scaled dock FaceCanvas (see <see cref="CardFace"/>) those
/// all render at card size automatically. A <see cref="ParticleSystem"/>, though,
/// renders through its OWN renderer (not the canvas) and is authored for the game's
/// full-size screen-space card: left in <c>World</c> simulation space with
/// <c>Local</c>/<c>Shape</c> scaling it sprays smoke at screen-pixel scale ACROSS THE
/// WHOLE DIORAMA. This clamp forces it card-local:
/// - <c>simulationSpace = Local</c> — particles ride the tiny card instead of being
///   emitted into world space and left behind at authored size.
/// - <c>scalingMode = Hierarchy</c> — start size/velocity inherit the card's world
///   scale, so the plume is sized to the card, not the play field.
///
/// Fully reversible: the pre-change module values are recorded per live instance and
/// put back when the instance changes/recycles or on <see cref="Detach"/> (widget
/// disable/destroy, module shutdown, hot reload). One card owns one of these.
/// </summary>
internal sealed class BurnCardFx
{
    private ParticleSystem? _bound;
    private ParticleSystemSimulationSpace _origSpace;
    private ParticleSystemScalingMode _origScaling;
    private bool _logged;

    /// <summary>
    /// Per-frame: track the card's live smoke instance and keep it card-bounded. Cheap
    /// (a reference compare) when nothing is burning — <c>_smokeEffect</c> is null
    /// outside an active burn/ghost effect.
    /// </summary>
    internal void Tick(FullAbilityCard? full)
    {
        CardEffects? effects = full != null ? full.cardEffects : null;
        ParticleSystem? smoke = effects != null ? effects._smokeEffect : null;

        if (ReferenceEquals(smoke, _bound))
            return;

        // The card's smoke instance changed (spawned, recycled, or swapped by the pool).
        RestoreBound();
        if (smoke != null)
            Bind(smoke);
    }

    /// <summary>Restore the tracked instance and drop it (disable/destroy/hot reload).</summary>
    internal void Detach() => RestoreBound();

    private void Bind(ParticleSystem smoke)
    {
        _bound = smoke;
        ParticleSystem.MainModule main = smoke.main;
        _origSpace = main.simulationSpace;
        _origScaling = main.scalingMode;

        bool changed = false;
        if (main.simulationSpace != ParticleSystemSimulationSpace.Local)
        {
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            changed = true;
        }
        if (main.scalingMode != ParticleSystemScalingMode.Hierarchy)
        {
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            changed = true;
        }

        if (changed && !_logged)
        {
            _logged = true;
            VRLog.Info("Cards", "Bounded burn CardSmoke particle to the card " +
                                $"(simulationSpace {_origSpace}->Local, scalingMode {_origScaling}->Hierarchy) " +
                                "— was spraying at screen scale across the diorama.");
        }
    }

    private void RestoreBound()
    {
        if (_bound == null)
        {
            _bound = null;
            return;
        }
        // Restore even if the pool has since disabled the instance — writing module
        // values on an inactive system is harmless and leaves the pooled prefab clean.
        ParticleSystem.MainModule main = _bound.main;
        main.simulationSpace = _origSpace;
        main.scalingMode = _origScaling;
        _bound = null;
    }
}
