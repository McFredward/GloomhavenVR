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
/// - REPARENT onto the world VR card (passed in as <c>cardTransform</c>): the smoke is
///   authored under the game's full-size SCREEN card, so <c>scalingMode = Hierarchy</c>
///   alone multiplies the plume by that huge screen-card scale (still diorama-covering).
///   Moving it under the small world card makes Hierarchy size the plume to the world card
///   AND anchors it at the burning card's world position.
/// - <c>simulationSpace = Local</c> — particles ride the tiny card instead of being
///   emitted into world space and left behind at authored size.
/// - <c>scalingMode = Hierarchy</c> — start size/velocity inherit the card's world
///   scale, so the plume is sized to the card, not the play field.
/// - start size/speed multipliers clamped down for extra shrink so the sparks stay small
///   and localized around the lying card.
///
/// Fully reversible: the pre-change parent + module values are recorded per live instance
/// and put back when the instance changes/recycles or on <see cref="Detach"/> (widget
/// disable/destroy, module shutdown, hot reload) — balanced across bind/unbind so a pooled
/// instance is left exactly as found. One card owns one of these.
/// </summary>
internal sealed class BurnCardFx
{
    private ParticleSystem? _bound;
    private ParticleSystemSimulationSpace _origSpace;
    private ParticleSystemScalingMode _origScaling;
    private Transform? _origParent;      // the smoke's parent before we reparented it (restored on unbind)
    private float _origStartSize;        // pre-clamp main.startSizeMultiplier
    private float _origStartSpeed;       // pre-clamp main.startSpeedMultiplier
    private bool _reparented;            // true while _bound lives under the world card
    private bool _logged;

    /// <summary>
    /// Extra shrink applied on top of the Hierarchy scaling: the game authors the plume
    /// for its full-size screen card, so even sized to the small world card the sparks
    /// read big. Clamp the start size/speed multipliers to keep them small and localized
    /// around the lying card. Restored verbatim on unbind.
    /// </summary>
    private const float StartSizeMultiplier = 0.35f;
    private const float StartSpeedMultiplier = 0.35f;

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
            Bind(smoke, cardTransform);
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

    // SpawnConsumedPlume(Transform) is REMOVED, together with its two consts ItemPlumeCardSpan
    // (1.4 card widths) and ItemPlumeMaxLifetime (1.4 s). It spawned the game's CardSmoke prefab on
    // a consumed ItemsPile chip. It had no call sites: aa63e87 removed both spawn sites and b9e48b5
    // routed the consumed look through the hosted card's OWN clamped effects instead.
    //
    // THE PLUME STAYS REMOVED. Re-adding it is a documented temptation, and here is the price of
    // getting it wrong (ITEM 10, "field-covering consumed-item fog"): the burning-ABILITY-card path
    // below (Bind) reparents the game's ONE managed smoke instance onto the small world card, so the
    // whole child hierarchy shrinks by the screen-card scale it leaves behind — every sub-emitter
    // comes down to card size for free. A spawn path has NO such reparent, and the first version
    // clamped only the FIRST ParticleSystem (GetComponentInChildren), leaving the prefab's OTHER
    // emitters at World simulation and authored screen scale, spraying across the whole diorama.
    // Any revival must clamp EVERY emitter (Local sim + Hierarchy scaling + the shrink), cap start
    // lifetime, AND pin the plume root's WORLD scale to a fixed card-relative size.

    private void Bind(ParticleSystem smoke, Transform cardTransform)
    {
        _bound = smoke;
        ParticleSystem.MainModule main = smoke.main;
        _origSpace = main.simulationSpace;
        _origScaling = main.scalingMode;
        _origStartSize = main.startSizeMultiplier;
        _origStartSpeed = main.startSpeedMultiplier;

        // ROOT CAUSE (test #22): the smoke stays a child of the game's full-size SCREEN-
        // SPACE card (CardEffects.transform), so scalingMode=Hierarchy multiplies the plume
        // by that huge screen-card scale → it covers the whole diorama. Reparent it onto the
        // small world VR card: Hierarchy then sizes the plume to the world card AND anchors
        // it at the burning card's world position. worldPositionStays:false lets it inherit
        // the world card's transform cleanly. Recorded so RestoreBound puts it back.
        if (cardTransform != null && smoke.transform.parent != cardTransform)
        {
            _origParent = smoke.transform.parent;
            smoke.transform.SetParent(cardTransform, worldPositionStays: false);
            _reparented = true;
        }

        if (main.simulationSpace != ParticleSystemSimulationSpace.Local)
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
        if (main.scalingMode != ParticleSystemScalingMode.Hierarchy)
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        // Extra shrink so the sparks stay small and localized around the lying card.
        main.startSizeMultiplier = _origStartSize * StartSizeMultiplier;
        main.startSpeedMultiplier = _origStartSpeed * StartSpeedMultiplier;

        if (!_logged)
        {
            _logged = true;
            VRLog.Info("Cards", "Bounded burn CardSmoke particle to the world card " +
                                $"(reparented {_reparented}, simulationSpace {_origSpace}->Local, " +
                                $"scalingMode {_origScaling}->Hierarchy, size/speed ×{StartSizeMultiplier:F2}) " +
                                "— was spraying at screen scale across the diorama.");
        }
    }

    private void RestoreBound()
    {
        if (_bound == null)
        {
            _reparented = false;
            _origParent = null;
            return;
        }
        // Restore even if the pool has since disabled the instance — writing module
        // values on an inactive system is harmless and leaves the pooled prefab clean.
        ParticleSystem.MainModule main = _bound.main;
        main.simulationSpace = _origSpace;
        main.scalingMode = _origScaling;
        main.startSizeMultiplier = _origStartSize;
        main.startSpeedMultiplier = _origStartSpeed;
        // Put the smoke back under its original card parent so the pooled instance is left
        // exactly as we found it (balanced with the reparent in Bind — no leak on recycle).
        if (_reparented)
            _bound.transform.SetParent(_origParent, worldPositionStays: false);
        _reparented = false;
        _origParent = null;
        _bound = null;
    }
}
