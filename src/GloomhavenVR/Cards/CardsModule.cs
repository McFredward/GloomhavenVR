using GloomhavenVR.Cards.Patches;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Phase 3b (feat/cards) — the Demeo card hand (ROADMAP R3):
/// 2D hand suppressed (visuals only — bookkeeping untouched, see
/// <see cref="Patches.HandSuppressionPatches"/> header for the strategy), palm-up fan
/// of live-canvas 3D cards, grab/inspect, play tray with initiative slot order +
/// swap, short/long-rest tokens, in-turn top/bottom half poking. All game entry
/// points verified against the real DLLs in <see cref="CardsGameApi"/>; blocking
/// select calls serialized through <see cref="CardActionQueue"/>.
///
/// Active when VR runs, and in Dev mode (desktop sim + [Cards] DevFakeHand).
/// Shutdown restores every re-parented card canvas and the 2D hand window
/// (hot-reload clean).
/// </summary>
internal sealed class CardsModule : IVRModule
{
    public string Name => "Cards";

    private GameObject? _driverGo;
    private bool _active;

    public void Init()
    {
        CardsConfig.Bind();

        if (!VRSession.IsRunning && !Plugin.DevMode.Value)
        {
            VRLog.Debug(Name, "VR not running and dev mode off — card hand not installed.");
            return;
        }

        _active = true;

        VRSession.Harmony?.PatchAll(typeof(CardsHandManager_ShowList_Patch));
        VRSession.Harmony?.PatchAll(typeof(CardsHandManager_ShowAll_Patch));
        VRSession.Harmony?.PatchAll(typeof(CardsHandManager_ShowHands_Patch));
        VRSession.Harmony?.PatchAll(typeof(CardsHandUI_OnDestroy_Patch));
        VRSession.Harmony?.PatchAll(typeof(CardsHandUI_DestroyCardUI_Patch));

        HandSuppression.Active = true;

        _driverGo = new GameObject("GloomhavenVR.Cards");
        Object.DontDestroyOnLoad(_driverGo);
        _driverGo.hideFlags = HideFlags.HideAndDontSave;
        _driverGo.AddComponent<CardsDriver>();

        VRLog.Info(Name, "Card hand installed (fan + tray + half selection + pile viewer; " +
                         "2D hand visually suppressed).");
    }

    public void Shutdown()
    {
        if (!_active)
            return;
        _active = false;

        if (_driverGo != null)
        {
            // OnDestroy on the driver restores adopted faces + destroys VR objects.
            Object.Destroy(_driverGo);
            _driverGo = null;
        }

        HandSuppression.Active = false;
        HandSuppression.Restore();
        CardsSignals.Clear();
        CardActionQueue.Clear();
        // Harmony patches are removed collectively by Plugin.OnDestroy (UnpatchSelf).
    }
}
