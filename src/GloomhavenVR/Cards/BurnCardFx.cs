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

    // Test #22 symptom 4c-ii: change-dedup for the "burn plays ON the card" diagnostic.
    private bool _effectActive;

    /// <summary>
    /// Per-frame: keep the card's live smoke instance card-bounded and log the
    /// on-card burn/ghost lifecycle. Cheap (reference + set membership) when nothing is
    /// burning — <c>_smokeEffect</c> is null and no effect is toggled outside a burn.
    /// </summary>
    /// <param name="full">The adopted game card (its <c>cardEffects</c> drives the burn).</param>
    /// <param name="cardTransform">The world-space VR card the face is kept on — its
    /// position is logged so the hardware log shows the burn rendered at the dock, not
    /// as a fullscreen flat presentation.</param>
    internal void Tick(FullAbilityCard? full, Transform cardTransform)
    {
        CardEffects? effects = full != null ? full.cardEffects : null;

        // Symptom 4c-ii evidence: the burn/ghost timeline runs on the card's OWN uGUI
        // (face-image dissolve + _uiFxOverlay flame) plus the bounded CardSmoke below —
        // all on this world card. Because the face is kept on our dock FaceCanvas (see
        // CardFace), it plays HERE, at the laid card's world position, and the view
        // returns to the two-card action-selection display once the card resolves.
        bool active = effects != null && (
            effects.HasEffect(CardEffects.FXTask.BurnCard)
            || effects.HasEffect(CardEffects.FXTask.LostMode)
            || effects.HasEffect(CardEffects.FXTask.DiscardMode));
        if (active != _effectActive)
        {
            _effectActive = active;
            if (active)
            {
                // ITEM 2: hold the game's flat screen-space hand/card down for the whole
                // burn so the 2D dissolve/flame can't leak onto the FlatScreen modal
                // mirror (see HandSuppression.BurnActive). The world-space smoke plume
                // bounded below still plays in VR to signal the burn.
                Patches.HandSuppression.BeginBurn();
                VRLog.Info("Cards", "Burn/ghost effect playing ON the dock card at world " +
                                    $"{cardTransform.position} (card mesh, not a fullscreen flat).");
            }
            else
            {
                Patches.HandSuppression.EndBurn();
            }
        }

        ParticleSystem? smoke = effects != null ? effects._smokeEffect : null;
        if (ReferenceEquals(smoke, _bound))
            return;

        // The card's smoke instance changed (spawned, recycled, or swapped by the pool).
        RestoreBound();
        if (smoke != null)
            Bind(smoke);
    }

    /// <summary>Restore the tracked instance and drop it (disable/destroy/hot reload).</summary>
    internal void Detach()
    {
        if (_effectActive)
        {
            _effectActive = false;
            Patches.HandSuppression.EndBurn(); // balance the BeginBurn from Tick
        }
        RestoreBound();
    }

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
