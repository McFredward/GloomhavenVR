using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// DAMAGE PREVIEW (bug #3). When a character takes damage they may LOSE/BURN cards
/// instead. In the FLAT game, during that choice the world-space <c>HealthBar</c> above
/// the mini highlights how many HP slots the incoming damage WOULD consume — a pulsing
/// preview region that repaints live as shields / active bonuses toggle. In VR that
/// preview is INVISIBLE, for two reasons the take-damage dock introduced:
///   1. FOCUS GATE — the pulsing overlay object (<c>HealthBar.m_PreviewBar</c>) is only
///      made active by <c>HealthBar.Focus(true)</c>, driven through
///      <c>WorldspacePanelUIController.Focus</c>. <c>PreviewAttack</c> writes the values
///      but never activates the object, so nothing shows unless something also asserts
///      Focus on the panel — which nothing in VR does.
///   2. HOVER STAND-DOWN — the flat game drives <c>PreviewSimpleDamage</c> off the
///      widgets' mouse-hover handlers, but <see cref="Patches.TakeDamagePanelSafety"/>
///      deliberately swallows every <c>OnMouseEnter*/OnMouseExit*</c> while the row is
///      docked (anti-jitter, test #23), so the hover that would repaint the preview
///      never fires.
///
/// This is a PRESENTATION-ONLY surface: it converts / moves nothing. It mirrors the flat
/// behaviour directly onto the world-space bar <see cref="ActorBars"/> already adopted —
/// the game keeps feeding the same live <c>HealthBar</c>, and the adopted controller is
/// the EXACT object the panel resolved for the attacked actor. While the take-damage row
/// is docked (<see cref="DecisionDockSurface.DockingTakeDamage"/>) and the panel has a
/// valid attacked actor, each tick it asserts <c>Focus(true, "VR_DAMAGE_PREVIEW")</c>
/// (activates the overlay) then <c>PreviewSimpleDamage(currentDamage, currentHealth)</c>
/// (repaints the pulsing cost region) — read via the panel's own publicized
/// <c>CalculateCurrentDamage()/CalculateCurrentHealth()</c> so we never re-derive damage
/// (standing rule, ActorBars.cs:310-313). Both calls are idempotent per tick:
/// <c>HighlightPreview</c> only starts the pulse tween when none is running, and
/// <c>PreviewSimpleDamage</c> merely re-writes slider values.
///
/// On undock / panel close / no valid actor it turns the preview OFF exactly once
/// (tracked by <see cref="_active"/>), mirroring <c>TakeDamagePanel.ResetPreviewing</c>:
/// <c>ResetDamagePreview(damageToTake)</c> collapses the cost region back to base health,
/// then <c>Focus(false, "VR_DAMAGE_PREVIEW")</c> drops our uniquely-namespaced focus
/// request so vanilla focus/opacity is restored (other focus reasons are untouched — the
/// request set is additive). The controller + values are cached at turn-on so the OFF
/// restore is clean even after the panel has already nulled <c>actorBeingAttacked</c>.
///
/// Gated on the same switches as its sibling dock (<see cref="WorldUIConfig.DecisionDock"/>
/// + <see cref="WorldUIConfig.ConversionActive"/> via <see cref="WorldSurface.WantConverted"/>),
/// so it is a strict no-op in the flat game and when VR is not running.
/// </summary>
internal sealed class DamagePreviewSurface : WorldSurface
{
    /// <summary>Namespaced focus request so we only ever add/remove OUR contribution to the panel's focus set.</summary>
    private const string FocusRequest = "VR_DAMAGE_PREVIEW";

    /// <summary>True while we are actively driving the preview (turn-off latch — flips OFF exactly once).</summary>
    private bool _active;

    /// <summary>The controller we are driving, cached at turn-on so the OFF restore hits the right bar even after the panel closed.</summary>
    private WorldspacePanelUIController? _activeController;

    /// <summary>The panel's base damage-to-take, cached at turn-on for the <c>ResetDamagePreview</c> restore.</summary>
    private int _activeDamageToTake;

    /// <summary>Attacked actor's name, cached at turn-on for the OFF log line.</summary>
    private string _activeActorName = "actor";

    public override string Name => "DamagePreview";

    // Same config toggle as the take-damage dock this rides along with.
    protected override bool ConfigEnabled => WorldUIConfig.DecisionDock.Value;

    // Presentation-only: nothing to convert or place. The base Tick is fully overridden.
    protected override RectTransform? FindTarget() => null;
    protected override void Place() { }

    public override void Tick()
    {
        // Base gate (config + ConversionActive + in-scenario) AND the take-damage row must
        // actually be docked. When either drops we fall to the turn-off path below.
        bool gate = WantConverted && !FlatScreen.ManualScreenActive && DecisionDockSurface.DockingTakeDamage;

        WorldspacePanelUIController? controller = null;
        int damage = 0, health = 0, damageToTake = 0;
        string actorName = "actor";

        if (gate)
        {
            TakeDamagePanel? panel = Singleton<TakeDamagePanel>.IsInitialized
                ? Singleton<TakeDamagePanel>.Instance
                : null;
            if (panel != null && panel.IsOpen && panel.actorBeingAttacked != null)
            {
                controller = ResolveController(panel);
                if (controller != null)
                {
                    damage = panel.CalculateCurrentDamage();
                    health = panel.CalculateCurrentHealth();
                    damageToTake = panel.damageToTake;
                    actorName = SafeActorName(panel.actorBeingAttacked);
                }
            }
        }

        if (controller != null)
        {
            // The attacked actor switched mid-drive (should not happen within one panel
            // session): cleanly reset the previous bar before adopting the new one.
            if (_active && !ReferenceEquals(controller, _activeController))
                TurnOff();

            // Activate the overlay object, THEN repaint the pulsing cost region. Both are
            // idempotent per tick (see the class doc), so this stays live across shield /
            // active-bonus toggles without restarting the pulse.
            controller.Focus(true, FocusRequest);
            controller.PreviewSimpleDamage(damage, health);

            _activeController = controller;
            _activeDamageToTake = damageToTake;
            _activeActorName = actorName;

            if (!_active)
            {
                _active = true;
                VRLog.Info("WorldUI", $"DAMAGE PREVIEW: on for '{actorName}' — world-space health bar now shows " +
                                      "the pulsing HP cost of the incoming damage (live across shield/bonus toggles).");
            }
        }
        else if (_active)
        {
            TurnOff();
        }
    }

    /// <summary>
    /// Restore vanilla exactly once: collapse the preview cost region back to base health
    /// (<c>ResetDamagePreview</c>) then drop our namespaced focus request. Uses the cached
    /// controller/value so it works even after the panel nulled its actor. Defensive: the
    /// controller may be Unity-dead (actor killed by lethal damage) and the game methods
    /// dereference the tracked actor, so guard + swallow.
    /// </summary>
    private void TurnOff()
    {
        WorldspacePanelUIController? controller = _activeController;
        if (controller != null)
        {
            try
            {
                controller.ResetDamagePreview(_activeDamageToTake);
                controller.Focus(false, FocusRequest);
            }
            catch (System.Exception ex)
            {
                VRLog.Warn("WorldUI", $"DAMAGE PREVIEW: off restore for '{_activeActorName}' skipped " +
                                      $"({ex.GetType().Name}) — bar likely already torn down.");
            }
        }
        VRLog.Info("WorldUI", $"DAMAGE PREVIEW: off for '{_activeActorName}' — preview restored to vanilla resting state.");
        _active = false;
        _activeController = null;
        _activeDamageToTake = 0;
    }

    /// <summary>
    /// The controller for the attacked actor: prefer the panel's own resolved
    /// <c>actorHUDController</c> (the EXACT instance <see cref="ActorBars"/> adopted), fall
    /// back to a lookup over the game's live panel registry by matching the attacked actor.
    /// </summary>
    private static WorldspacePanelUIController? ResolveController(TakeDamagePanel panel)
    {
        WorldspacePanelUIController? controller = panel.actorHUDController;
        if (controller != null)
            return controller;

        var attacked = panel.actorBeingAttacked;
        WorldspaceUITools tools = WorldspaceUITools.Instance;
        if (attacked == null || tools == null)
            return null;

        List<WorldspacePanelUIController> registry = tools._panelUIControllers;
        for (int i = 0; i < registry.Count; i++)
        {
            WorldspacePanelUIController candidate = registry[i];
            if (candidate != null && candidate.InfoUI != null && candidate.InfoUI.m_ActorBehavior != null
                && candidate.InfoUI.m_ActorBehavior.Actor != null
                && candidate.InfoUI.m_ActorBehavior.Actor.Equals(attacked))
                return candidate;
        }
        return null;
    }

    private static string SafeActorName(CActor actor)
    {
        try
        {
            // CActor.ActorLocKey() — the same handle the game logs bars by (CActor.cs:1386).
            string? key = actor.ActorLocKey();
            return string.IsNullOrEmpty(key) ? "actor" : key;
        }
        catch (System.Exception)
        {
            return "actor";
        }
    }

    public override void Shutdown()
    {
        base.Shutdown();
        if (_active)
            TurnOff();
    }
}
