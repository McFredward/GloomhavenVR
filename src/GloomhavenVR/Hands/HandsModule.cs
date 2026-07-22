using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// Phase 2 (feat/hands): hand presence and interaction primitives — hand models
/// (bundle gloves or procedural fallback), finger curling, poke/ray/grab/palm-gate,
/// haptics. The types under <c>GloomhavenVR.Hands(.Interact)</c> are the FROZEN
/// interface consumed by Cards/Board/WorldUI (docs/INTERFACES-P2.md).
///
/// Active when VR runs, and in Dev mode for the desktop hand simulation.
/// </summary>
internal sealed class HandsModule : IVRModule
{
    public string Name => "Hands";

    private GameObject? _driverGo;

    public void Init()
    {
        // [Hands] curl/fist tunables + TestFist debug toggle (module-own cfg file).
        // Bound before any VRHand/FingerCurler exists; all readers are null-safe anyway.
        HandsConfig.Bind();

        // Menu2D per-hand policy (hardware test #6, requirement 4): only the DOMINANT
        // hand carries the laser — it is the flat-screen pointer/click hand; the
        // non-dominant hand keeps Poke (settings panel, flat-screen poke-click) and
        // its TRIGGER switches dominance (FlatScreen.TickHandednessSwitch). Matches
        // the BoardTargeting dominant-only-laser rule (docs/INTERFACES-P2.md §4).
        // Registered via the state machine's extension API — never patch the matrix.
        Core.Events.VRModeStateMachine.SetHandInteractorPolicy(
            Core.Events.VRMode.Menu2D, Core.Events.HandRole.Dominant,
            Core.Events.Interactors.Ray | Core.Events.Interactors.Poke);
        Core.Events.VRModeStateMachine.SetHandInteractorPolicy(
            Core.Events.VRMode.Menu2D, Core.Events.HandRole.NonDominant,
            Core.Events.Interactors.Poke);

        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
        {
            VRLog.Debug(Name, "VR not running and dev mode off — hands not installed.");
            return;
        }

        _driverGo = new GameObject("GloomhavenVR.HandsDriver");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        _driverGo.AddComponent<HandsDriver>();

        VRLog.Info(Name, "Hands driver installed — waiting for the VR rig (or dev simulation).");
    }

    public void Shutdown()
    {
        // Hot-reload hygiene: drop our per-hand overrides (statics survive Shutdown
        // within one assembly load; symmetric with Init).
        Core.Events.VRModeStateMachine.ClearHandInteractorPolicy(
            Core.Events.VRMode.Menu2D, Core.Events.HandRole.Dominant);
        Core.Events.VRModeStateMachine.ClearHandInteractorPolicy(
            Core.Events.VRMode.Menu2D, Core.Events.HandRole.NonDominant);

        if (_driverGo != null)
        {
            Object.Destroy(_driverGo);
            _driverGo = null;
        }

        // Hot-reload hygiene: registries and the asset bundle are static.
        VRInteractables.Clear();
        UguiPokeSurfaces.Clear();
        RayInteractor.ClearUiTargets();
        HandVisuals.UnloadBundle();
    }
}
