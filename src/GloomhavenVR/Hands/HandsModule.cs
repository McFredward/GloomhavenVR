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
